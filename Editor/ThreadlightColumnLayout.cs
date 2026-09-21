namespace Threadlight.EditorUI
{
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Retained two-column layout for the column layout view. Slots are mounted once
/// in canonical navigation order; responsive and height-balancing changes only
/// update their coordinates, preserving focus and panel lifecycle state.
/// </summary>
public class ThreadlightColumnLayout : VisualElement
{
    public readonly struct Placement
    {
        public Placement(float left, float top, float width)
        {
            Left = left;
            Top = top;
            Width = width;
        }

        public float Left { get; }
        public float Top { get; }
        public float Width { get; }
    }

    public readonly struct PlacementResult
    {
        public PlacementResult(Placement[] placements, float contentHeight)
        {
            Placements = placements;
            ContentHeight = contentHeight;
        }

        public Placement[] Placements { get; }
        public float ContentHeight { get; }
    }

    private readonly string stackedClassName;
    public ThreadlightColumnLayout(string stackedClassName = "threadlight-column-layout--stacked") {
        this.stackedClassName = stackedClassName;
        style.position = Position.Relative;
        style.minWidth = 0f;
    }
    private readonly List<VisualElement> slots = new List<VisualElement>();
    private bool? stacked;
    private float cachedWidth = float.NaN;
    private float cachedGap = float.NaN;
    private float cachedScale = float.NaN;
    private float[] cachedHeights = Array.Empty<float>();
    private int[] cachedColumns = Array.Empty<int>();
    private bool bound;

    public IReadOnlyList<VisualElement> Slots => slots;
    public bool IsStacked => stacked ?? true;

    private readonly List<VisualElement> measuredContent = new List<VisualElement>();
    private float[] measuredHeights = Array.Empty<float>();
    private int[] assignedColumns = Array.Empty<int>();
    private float boundBreakpoint, boundGap;
    private bool applying, rebalance = true;
    private EditorApplication.CallbackFunction pending;

    /// <summary>Measure retained content without reparenting controls.</summary>
    public void BindContents(IReadOnlyList<VisualElement> content, float breakpoint = 840f, float gap = 14f)
    {
        if (bound) throw new InvalidOperationException("This column layout is already bound.");
        ValidateContents(slots, content);
        bound = true;
        boundBreakpoint = breakpoint;
        boundGap = gap;
        measuredHeights = new float[content.Count];
        assignedColumns = cachedColumns.Length == content.Count
            ? Copy(cachedColumns) : new int[content.Count];
        foreach (VisualElement item in content)
        {
            measuredContent.Add(item);
            item.RegisterCallback<GeometryChangedEvent>(OnContentGeometryChanged);
        }
        if (cachedColumns.Length != content.Count)
            AssignInitialColumns();
        RegisterCallback<GeometryChangedEvent>(evt => {
            if (evt.oldRect.width != evt.newRect.width) RefreshContents();
        });
        RegisterCallback<AttachToPanelEvent>(evt => { if (evt.target == this) QueueRefresh(); });
        RegisterCallback<DetachFromPanelEvent>(evt => {
            if (evt.target != this || pending == null) return;
            EditorApplication.delayCall -= pending;
            pending = null;
        });
        QueueRefresh();
    }

    /// <summary>
    /// Retains surviving slots and their lanes, mounting or removing only changed entries.
    /// Each content element must already belong to its corresponding slot.
    /// </summary>
    public void ReconcileContents(IReadOnlyList<VisualElement> orderedSlots,
        IReadOnlyList<VisualElement> content)
    {
        if (!bound) throw new InvalidOperationException("Bind the column layout before reconciling it.");
        ValidateContents(orderedSlots, content);
        // Snapshot inputs: callers may pass Slots itself as the requested ordering.
        var nextSlots = new List<VisualElement>(orderedSlots);
        var nextContent = new List<VisualElement>(content);
        var retainedColumns = new Dictionary<VisualElement, int>();
        var retainedHeights = new Dictionary<VisualElement, float>();
        for (int i = 0; i < slots.Count; i++)
        {
            retainedColumns.Add(slots[i],
                (cachedColumns.Length == slots.Count ? cachedColumns[i] : assignedColumns[i]) == 1 ? 1 : 0);
            retainedHeights.Add(slots[i], cachedHeights.Length == slots.Count ? cachedHeights[i] : measuredHeights[i]);
        }
        float[] nextHeights = new float[nextSlots.Count];
        int[] nextColumns = new int[nextSlots.Count];
        float[] totals = new float[2];
        for (int i = 0; i < nextSlots.Count; i++)
        {
            nextHeights[i] = MeasureHeight(nextContent[i]);
            if (float.IsNaN(nextContent[i].layout.height) &&
                retainedHeights.TryGetValue(nextSlots[i], out float priorHeight))
                nextHeights[i] = priorHeight;
            if (retainedColumns.TryGetValue(nextSlots[i], out int lane))
            {
                nextColumns[i] = lane;
                totals[lane] += nextHeights[i];
            }
        }
        for (int i = 0; i < nextSlots.Count; i++)
        {
            if (retainedColumns.ContainsKey(nextSlots[i])) continue;
            int lane = totals[0] <= totals[1] ? 0 : 1;
            nextColumns[i] = lane;
            totals[lane] += Mathf.Max(1f, nextHeights[i]);
        }
        applying = true;
        try
        {
            foreach (VisualElement item in measuredContent)
                item.UnregisterCallback<GeometryChangedEvent>(OnContentGeometryChanged);
            var nextSet = new HashSet<VisualElement>(nextSlots);
            foreach (VisualElement slot in slots)
                if (!nextSet.Contains(slot)) hierarchy.Remove(slot);
            for (int i = 0; i < nextSlots.Count; i++)
            {
                VisualElement slot = nextSlots[i];
                if (slot.parent == null) hierarchy.Insert(i, slot);
                else if (hierarchy[i] != slot) slot.PlaceBehind(hierarchy[i]);
            }
            slots.Clear();
            slots.AddRange(nextSlots);
            measuredContent.Clear();
            measuredContent.AddRange(nextContent);
            measuredHeights = nextHeights;
            assignedColumns = nextColumns;
            // Once entries are retained, their lanes remain authoritative even if
            // reconciliation happens during the first narrow layout.
            rebalance = false;
            foreach (VisualElement item in measuredContent)
                item.RegisterCallback<GeometryChangedEvent>(OnContentGeometryChanged);
            InvalidatePlacementCache();
        }
        finally { applying = false; }
        RefreshContents();
        QueueRefresh();
    }

    private void ValidateContents(IReadOnlyList<VisualElement> orderedSlots,
        IReadOnlyList<VisualElement> content)
    {
        if (orderedSlots == null || content == null || orderedSlots.Count != content.Count)
            throw new ArgumentException("Content must match the mounted slots.", nameof(content));
        var uniqueSlots = new HashSet<VisualElement>();
        var uniqueContent = new HashSet<VisualElement>();
        for (int i = 0; i < orderedSlots.Count; i++)
        {
            VisualElement slot = orderedSlots[i], item = content[i];
            if (slot == null || item == null || slot == this || slot.Contains(this) ||
                !uniqueSlots.Add(slot) || !uniqueContent.Add(item) ||
                (slot.parent != null && (slot.parent != this || !slots.Contains(slot))) ||
                (item != slot && !slot.Contains(item)))
                throw new ArgumentException("Each unique content element must belong to its unique layout slot.", nameof(content));
        }
    }

    private static float MeasureHeight(VisualElement item)
    {
        if (item.resolvedStyle.display == DisplayStyle.None) return 0f;
        float height = item.layout.height + item.resolvedStyle.marginTop + item.resolvedStyle.marginBottom;
        return float.IsNaN(height) || float.IsInfinity(height) ? 0f : Mathf.Max(0f, height);
    }

    private void OnContentGeometryChanged(GeometryChangedEvent evt)
    {
        if (evt.oldRect.width != evt.newRect.width ||
            (contentRect.width >= boundBreakpoint && evt.oldRect.height != evt.newRect.height))
            RefreshContents();
    }

    private void AssignInitialColumns()
    {
        var estimates = new float[measuredContent.Count];
        for (int i = 0; i < estimates.Length; i++)
            estimates[i] = Mathf.Max(1f, MeasureHeight(measuredContent[i]));
        AssignBalancedColumns(estimates, assignedColumns);
    }

    private void RefreshContents()
    {
        if (applying || panel == null) return;
        float width = contentRect.width;
        if (!IsFinitePositive(width)) return;
        applying = true;
        try
        {
            for (int i = 0; i < measuredContent.Count; i++)
                measuredHeights[i] = MeasureHeight(measuredContent[i]);
            if (rebalance)
            {
                AssignInitialColumns();
                // Verify the actual slot width, not merely a smaller stale child
                // width. Hidden or empty content does not prevent lane settlement.
                float scale = Mathf.Max(1f, EditorGUIUtility.pixelsPerPoint);
                float firstWidth = Mathf.Floor(Mathf.Max(0f, width - boundGap) * .5f * scale) / scale;
                bool measuredColumns = width >= boundBreakpoint;
                for (int i = 0; i < slots.Count; i++)
                    measuredColumns &= Mathf.Abs(slots[i].layout.width - firstWidth) <= 1f;
                rebalance = !measuredColumns;
            }
            Apply(width, boundBreakpoint, boundGap, EditorGUIUtility.pixelsPerPoint,
                measuredHeights, assignedColumns);
        }
        finally { applying = false; }
    }

    private void QueueRefresh()
    {
        if (pending != null || panel == null) return;
        pending = () => {
            EditorApplication.delayCall -= pending;
            pending = null;
            RefreshContents();
        };
        EditorApplication.delayCall += pending;
    }

    public static int[] GetBalancedColumns(IReadOnlyList<float> heights)
    {
        if (heights == null || heights.Count == 0) return Array.Empty<int>();
        int[] columns = new int[heights.Count];
        AssignBalancedColumns(heights, columns);
        return columns;
    }

    private static void AssignBalancedColumns(IReadOnlyList<float> heights, int[] columns)
    {
        float left = 0f, right = 0f;
        for (int i = 0; i < heights.Count; i++)
        {
            int column = left <= right ? 0 : 1;
            columns[i] = column;
            float height = heights[i];
            if (float.IsNaN(height) || float.IsInfinity(height)) continue;
            if (column == 0) left += Mathf.Max(0f, height);
            else right += Mathf.Max(0f, height);
        }
    }

    public void AddSlot(VisualElement slot)
    {
        if (bound) throw new InvalidOperationException("Use ReconcileContents after binding the layout.");
        if (slot == null)
            throw new ArgumentNullException(nameof(slot));
        if (slot.parent != null)
            throw new InvalidOperationException(
                "column layout slots must be added before they are mounted.");
        slots.Add(slot);
        hierarchy.Add(slot);
        InvalidatePlacementCache();
    }

    public bool Apply(
        float availableWidth,
        float breakpoint,
        float columnGap,
        float pixelsPerPoint,
        IReadOnlyList<float> heights,
        IReadOnlyList<int> columns)
    {
        bool useStacked = !IsFinitePositive(availableWidth) ||
            availableWidth < breakpoint;
        if (heights == null || heights.Count != slots.Count)
            throw new ArgumentException(
                "A measured height is required for every column layout slot.",
                nameof(heights));
        if (!useStacked && (columns == null || columns.Count != slots.Count))
            throw new ArgumentException(
                "A column assignment is required for every column layout slot.",
                nameof(columns));
        if (MatchesCachedLayout(
                useStacked, availableWidth, columnGap, pixelsPerPoint,
                heights, columns))
        {
            return false;
        }

        stacked = useStacked;
        EnableInClassList(
            stackedClassName,
            useStacked);
        if (useStacked)
        {
            style.height = StyleKeyword.Auto;
            foreach (VisualElement slot in slots)
            {
                slot.style.position = Position.Relative;
                slot.style.left = StyleKeyword.Auto;
                slot.style.top = StyleKeyword.Auto;
                slot.style.width = StyleKeyword.Auto;
            }
        }
        else
        {
            PlacementResult result = CalculatePlacements(
                heights,
                columns,
                availableWidth,
                columnGap,
                pixelsPerPoint);
            for (int index = 0; index < slots.Count; index++)
            {
                VisualElement slot = slots[index];
                Placement placement = result.Placements[index];
                slot.style.position = Position.Absolute;
                slot.style.left = placement.Left;
                slot.style.top = placement.Top;
                slot.style.width = placement.Width;
            }
            style.height = result.ContentHeight;
        }
        CacheLayout(
            availableWidth, columnGap, pixelsPerPoint, heights, columns);
        return true;
    }

    public static PlacementResult CalculatePlacements(
        IReadOnlyList<float> heights,
        IReadOnlyList<int> columns,
        float availableWidth,
        float columnGap,
        float pixelsPerPoint)
    {
        if (heights == null)
            throw new ArgumentNullException(nameof(heights));
        if (columns == null || columns.Count != heights.Count)
            throw new ArgumentException(
                "Column assignments must match measured heights.",
                nameof(columns));
        float gap = Mathf.Max(0f, columnGap);
        float scale = Mathf.Max(1f, pixelsPerPoint);
        float usableWidth = Mathf.Max(0f, availableWidth - gap);
        float firstWidth = Mathf.Floor(usableWidth * 0.5f * scale) / scale;
        float[] widths = { firstWidth, usableWidth - firstWidth };
        float[] tops = { 0f, 0f };
        Placement[] placements = new Placement[heights.Count];
        for (int index = 0; index < heights.Count; index++)
        {
            int column = columns[index] == 1 ? 1 : 0;
            float left = column == 0 ? 0f : widths[0] + gap;
            placements[index] = new Placement(left, tops[column], widths[column]);
            float height = heights[index];
            if (!float.IsNaN(height) && !float.IsInfinity(height))
                tops[column] += Mathf.Max(0f, height);
        }
        return new PlacementResult(placements, Mathf.Max(tops[0], tops[1]));
    }

    private bool MatchesCachedLayout(
        bool useStacked,
        float width,
        float gap,
        float scale,
        IReadOnlyList<float> heights,
        IReadOnlyList<int> columns)
    {
        if (stacked != useStacked || !Approximately(cachedWidth, width) ||
            !Approximately(cachedGap, gap) || !Approximately(cachedScale, scale) ||
            cachedHeights.Length != heights.Count ||
            (!useStacked && cachedColumns.Length != columns.Count))
        {
            return false;
        }
        for (int index = 0; index < heights.Count; index++)
        {
            if (!Approximately(cachedHeights[index], heights[index]) ||
                (!useStacked && cachedColumns[index] != columns[index]))
            {
                return false;
            }
        }
        return true;
    }

    private void CacheLayout(
        float width,
        float gap,
        float scale,
        IReadOnlyList<float> heights,
        IReadOnlyList<int> columns)
    {
        cachedWidth = width;
        cachedGap = gap;
        cachedScale = scale;
        cachedHeights = Copy(heights);
        cachedColumns = columns != null ? Copy(columns) : Array.Empty<int>();
    }

    private void InvalidatePlacementCache()
    {
        stacked = null;
        cachedWidth = float.NaN;
        cachedHeights = Array.Empty<float>();
        cachedColumns = Array.Empty<int>();
    }

    private static float[] Copy(IReadOnlyList<float> values)
    {
        float[] copy = new float[values.Count];
        for (int index = 0; index < values.Count; index++)
            copy[index] = values[index];
        return copy;
    }

    private static int[] Copy(IReadOnlyList<int> values)
    {
        int[] copy = new int[values.Count];
        for (int index = 0; index < values.Count; index++)
            copy[index] = values[index];
        return copy;
    }

    private static bool Approximately(float left, float right) =>
        (float.IsNaN(left) && float.IsNaN(right)) ||
        Mathf.Abs(left - right) <= 0.01f;

    private static bool IsFinitePositive(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value > 0.5f;
}
}
