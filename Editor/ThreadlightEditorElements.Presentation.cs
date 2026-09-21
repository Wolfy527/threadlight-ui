namespace Threadlight.EditorUI
{
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

public static partial class ThreadlightEditorElements
{
    /// <summary>
    /// Makes a retained diagnostic surface activate like a link without turning
    /// its severity presentation into a button. Accessory buttons keep their own
    /// action and do not also trigger navigation.
    /// </summary>
    public static void RegisterNavigationActivation(VisualElement surface, Action activate)
    {
        if (surface == null || activate == null)
            return;
        surface.focusable = true;
        surface.tabIndex = 0;
        surface.AddToClassList("threadlight-diagnostic-navigation");
        surface.style.position = Position.Relative;
        surface.style.paddingRight = 24f;
        Label affordance = new Label("›") { pickingMode = PickingMode.Ignore };
        affordance.name = "threadlight-diagnostic-navigation-affordance";
        affordance.AddToClassList("threadlight-diagnostic-navigation-affordance");
        affordance.style.position = Position.Absolute;
        affordance.style.right = 8f;
        affordance.style.top = 6f;
        affordance.style.unityFontStyleAndWeight = FontStyle.Bold;
        RegisterInteractionState(surface, (hovered, focused) =>
            affordance.style.color = hovered || focused
                ? ThreadlightEditorTheme.Accent
                : ThreadlightEditorTheme.TextMuted);
        surface.Add(affordance);
        surface.RegisterCallback<PointerUpEvent>(evt =>
        {
            VisualElement target = evt.target as VisualElement;
            if (evt.button != 0)
                return;
            for (VisualElement current = target; current != null && current != surface;
                 current = current.parent)
            {
                if (current is Button)
                    return;
            }
            activate();
            evt.StopPropagation();
        });
        surface.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (evt.target != surface || evt.keyCode != KeyCode.Return &&
                evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space)
                return;
            activate();
            evt.StopPropagation();
            evt.PreventDefault();
        });
    }

    /// <summary>
    /// Computes a comfortable vertical destination while excluding a measured
    /// fixed region. All rectangles use the same world-coordinate space.
    /// </summary>
    public static bool TryCalculateDiagnosticScrollOffset(
        Rect viewport, Rect target, Rect? occluder, float currentOffset,
        float maximumOffset, out float destinationOffset)
    {
        destinationOffset = currentOffset;
        if (!UsableRect(viewport) || !UsableRect(target) ||
            !UsableNumber(currentOffset) || !UsableNumber(maximumOffset))
            return false;
        float usableTop = viewport.yMin;
        float usableBottom = viewport.yMax;
        if (occluder.HasValue && UsableRect(occluder.Value) &&
            occluder.Value.xMax > viewport.xMin && occluder.Value.xMin < viewport.xMax &&
            occluder.Value.yMax > viewport.yMin && occluder.Value.yMin < viewport.yMax)
        {
            Rect blocked = occluder.Value;
            if (blocked.center.y >= viewport.center.y)
                usableBottom = Mathf.Min(usableBottom, blocked.yMin);
            else
                usableTop = Mathf.Max(usableTop, blocked.yMax);
        }
        float usableHeight = usableBottom - usableTop;
        if (usableHeight <= 12f)
            return false;
        float comfortableTop = usableTop + usableHeight * .16f;
        float comfortableBottom = usableTop + usableHeight * .66f;
        if (target.yMin >= comfortableTop && target.yMax <= comfortableBottom)
            return true;
        float preferredTop = usableTop + usableHeight * .27f;
        destinationOffset = Mathf.Clamp(
            currentOffset + target.yMin - preferredTop,
            0f,
            Mathf.Max(0f, maximumOffset));
        return true;
    }

    public static bool ScrollDiagnosticIntoSafeBand(
        ScrollView scroll, VisualElement target, VisualElement occluder = null)
    {
        if (scroll?.contentViewport == null || target == null || target.panel == null)
            return false;
        Rect? blocked = occluder != null && occluder.panel == target.panel
            ? occluder.worldBound
            : (Rect?)null;
        if (!TryCalculateDiagnosticScrollOffset(
                scroll.contentViewport.worldBound, target.worldBound, blocked,
                scroll.scrollOffset.y, scroll.verticalScroller.highValue,
                out float destination))
            return false;
        scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, destination);
        return true;
    }

    private static bool UsableRect(Rect value) =>
        UsableNumber(value.x) && UsableNumber(value.y) &&
        UsableNumber(value.width) && UsableNumber(value.height) &&
        value.width > 0f && value.height > 0f;

    private static bool UsableNumber(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>
    /// Keeps a studio's controls readable below its minimum working width.
    /// The standard horizontal scrollbar exposes the overflow; the retained
    /// workspace remains viewport-height so nested vertical scrolls and docks work.
    /// </summary>
    public static ScrollView CreateMinimumWidthViewport(VisualElement workspace, float minimumWidth)
    {
        if (workspace == null) throw new ArgumentNullException(nameof(workspace));
        if (workspace.parent != null)
            throw new ArgumentException("Mount the workspace through its viewport only once.", nameof(workspace));
        if (float.IsNaN(minimumWidth) || float.IsInfinity(minimumWidth) || minimumWidth <= 0f)
            throw new ArgumentOutOfRangeException(nameof(minimumWidth));
        var scroll = new ScrollView(ScrollViewMode.Horizontal);
        scroll.AddToClassList("threadlight-minimum-width-viewport");
        scroll.style.flexGrow = 1f;
        scroll.style.flexShrink = 1f;
        scroll.style.minWidth = 0f;
        scroll.style.minHeight = 0f;
        scroll.horizontalScrollerVisibility = ScrollerVisibility.Auto;
        scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
        VisualElement content = scroll.contentContainer;
        content.style.minWidth = minimumWidth;
        content.style.width = Length.Percent(100f);
        content.style.height = Length.Percent(100f);
        content.style.flexGrow = 0f;
        content.style.flexShrink = 0f;
        content.style.flexDirection = FlexDirection.Column;
        content.style.alignItems = Align.Stretch;
        workspace.style.flexGrow = 1f;
        workspace.style.flexShrink = 1f;
        workspace.style.minHeight = 0f;
        scroll.Add(workspace);
        return scroll;
    }

    /// <summary>Responsive group slots preserve each child's own frame and hierarchy.</summary>
    public static VisualElement CreateResponsiveGrid(string classPrefix, float breakpoint,
        params VisualElement[] groups)
    {
        VisualElement grid = new VisualElement(); grid.AddToClassList(classPrefix);
        List<VisualElement> visibleGroups = new List<VisualElement>();
        foreach (VisualElement group in groups)
        {
            if (group != null) visibleGroups.Add(group);
        }
        for (int index = 0; index < visibleGroups.Count; index++)
        {
            VisualElement group = visibleGroups[index];
            // Layout padding and divider rules belong to a slot, not the
            // contained folder's frame (whose left/top borders must survive).
            VisualElement column = new VisualElement();
            column.AddToClassList(classPrefix + "-column");
            if (index > 0) column.AddToClassList(classPrefix + "-column--secondary");
            if (index == visibleGroups.Count - 1)
                column.AddToClassList(classPrefix + "-column--last");
            column.Add(group);
            grid.Add(column);
        }
        BindWidthClass(grid, classPrefix + "--stacked", breakpoint);
        return grid;
    }

    /// <summary>Reserve space for a floating footer using its measured height.</summary>
    public static void BindFooterInset(VisualElement footer, VisualElement content,
        float initialInset = 96f, float gap = 22f) {
        if (footer == null || content == null) return;
        content.style.paddingBottom = initialInset;
        footer.RegisterCallback<GeometryChangedEvent>(evt => {
            float height = evt.newRect.height;
            if (!float.IsNaN(height) && !float.IsInfinity(height) && height > 0f)
                content.style.paddingBottom = Mathf.Ceil(height) + gap;
        });
    }

    public const string ReducedMotionClassName = "threadlight-reduced-motion";

    private sealed class MotionPreferenceBinding
    {
        public Action Apply;
        public bool Subscribed;
    }

    private static readonly ConditionalWeakTable<VisualElement, MotionPreferenceBinding>
        MotionPreferenceBindings =
            new ConditionalWeakTable<VisualElement, MotionPreferenceBinding>();

    /// <summary>
    /// Keeps one visual tree synchronized with the shared motion preference.
    /// The preference event is held only while the tree is attached to a panel.
    /// </summary>
    public static void BindMotionPreference(VisualElement root)
    {
        if (root == null || MotionPreferenceBindings.TryGetValue(root, out _))
            return;

        MotionPreferenceBinding binding = new MotionPreferenceBinding();
        binding.Apply = () =>
        {
            bool reduced = ThreadlightEditorPreferences.ReducedMotion;
            root.EnableInClassList(ReducedMotionClassName, reduced);
            if (!reduced)
                return;
            VisualElement pointerLight = root.panel?.visualTree?.Q<VisualElement>(
                "wolfy-pointer-light");
            if (pointerLight != null)
                pointerLight.style.opacity = 0f;
        };
        MotionPreferenceBindings.Add(root, binding);
        root.RegisterCallback<AttachToPanelEvent>(evt =>
        {
            if (evt.target != root)
                return;
            SubscribeMotionPreference(binding);
            binding.Apply();
        });
        root.RegisterCallback<DetachFromPanelEvent>(evt =>
        {
            if (evt.target == root)
                UnsubscribeMotionPreference(binding);
        });
        binding.Apply();
        if (root.panel != null)
            SubscribeMotionPreference(binding);
    }

    /// <summary>
    /// Gives raster icons a consistent layout box while preserving optical
    /// breathing room around glyphs with different silhouettes.
    /// </summary>
    public static void StyleOpticalIcon(Image icon, float slotSize, float opticalSize)
    {
        if (icon == null)
            return;
        slotSize = Mathf.Max(1f, slotSize);
        opticalSize = Mathf.Clamp(opticalSize, 1f, slotSize);
        float inset = (slotSize - opticalSize) * .5f;
        icon.scaleMode = ScaleMode.ScaleToFit;
        icon.style.width = icon.style.minWidth = icon.style.maxWidth = slotSize;
        icon.style.height = icon.style.minHeight = icon.style.maxHeight = slotSize;
        icon.style.paddingLeft = icon.style.paddingRight = inset;
        icon.style.paddingTop = icon.style.paddingBottom = inset;
        icon.style.flexShrink = 0f;
    }

    /// <summary>Fits the shared horizontal brand mark without icon-style square padding.</summary>
    public static void StyleWordmark(Image wordmark, float width, float height)
    {
        if (wordmark == null)
            return;
        width = Mathf.Max(1f, width);
        height = Mathf.Max(1f, height);
        wordmark.scaleMode = ScaleMode.ScaleToFit;
        wordmark.style.width = wordmark.style.minWidth = wordmark.style.maxWidth = width;
        wordmark.style.height = wordmark.style.minHeight = wordmark.style.maxHeight = height;
        wordmark.style.alignSelf = Align.Center;
        wordmark.style.flexShrink = 0f;
    }

    private static void SubscribeMotionPreference(MotionPreferenceBinding binding)
    {
        if (binding.Subscribed)
            return;
        ThreadlightEditorPreferences.ReducedMotionChanged += binding.Apply;
        binding.Subscribed = true;
    }

    private static void UnsubscribeMotionPreference(MotionPreferenceBinding binding)
    {
        if (!binding.Subscribed)
            return;
        ThreadlightEditorPreferences.ReducedMotionChanged -= binding.Apply;
        binding.Subscribed = false;
    }
}

/// <summary>A finite, non-picking destination cue owned by an Editor window.</summary>
public sealed class ThreadlightDestinationPulse
{
    private int generation;
    private VisualElement root;
    private VisualElement cue;
    private readonly List<VisualElement> geometryOwners = new List<VisualElement>();
    private readonly List<ScrollView> scrollOwners = new List<ScrollView>();
    private EventCallback<GeometryChangedEvent> geometryChanged;
    private EventCallback<DetachFromPanelEvent> detached;
    private Action<float> scrolled;
    private Action<Action, VisualElement, int> deferAction;
    public VisualElement Target { get; private set; }
    public bool Active => cue != null;

    public void Start(VisualElement root, VisualElement target, Color accent,
        Action<Action, VisualElement, int> defer, bool reducedMotion)
    {
        Cancel();
        if (root == null || target == null || root.panel == null ||
            target.panel != root.panel || defer == null)
            return;
        int token = generation;
        this.root = root;
        Target = target;
        deferAction = defer;
        cue = new VisualElement { pickingMode = PickingMode.Ignore };
        cue.name = "threadlight-diagnostic-destination-pulse";
        cue.AddToClassList("threadlight-diagnostic-destination-pulse");
        cue.style.position = Position.Absolute;
        cue.style.borderTopWidth = cue.style.borderRightWidth = 2f;
        cue.style.borderBottomWidth = cue.style.borderLeftWidth = 2f;
        cue.style.borderTopColor = cue.style.borderRightColor = accent;
        cue.style.borderBottomColor = cue.style.borderLeftColor = accent;
        cue.style.borderTopLeftRadius = cue.style.borderTopRightRadius = 7f;
        cue.style.borderBottomLeftRadius = cue.style.borderBottomRightRadius = 7f;
        cue.style.backgroundColor = new Color(accent.r, accent.g, accent.b, .13f);
        cue.style.opacity = 1f;
        root.Add(cue);
        RegisterMovementTracking();
        if (!RefreshBounds())
            return;
        if (!reducedMotion)
        {
            defer(() => SetOpacity(token, .28f), root, 160);
            defer(() => SetOpacity(token, .92f), root, 310);
        }
        defer(() => Clear(token), root, reducedMotion ? 280 : 560);
    }

    public void Cancel()
    {
        Cancel(true);
    }

    private void Cancel(bool removeCue)
    {
        generation++;
        UnregisterMovementTracking();
        if (removeCue && cue?.parent != null)
            cue.RemoveFromHierarchy();
        root = null;
        cue = null;
        Target = null;
        deferAction = null;
    }

    private void RegisterMovementTracking()
    {
        geometryChanged = _ => RefreshBounds();
        detached = _ => CancelFromDetach();
        scrolled = _ => RefreshBounds();
        for (VisualElement current = Target; current != null; current = current.parent)
        {
            geometryOwners.Add(current);
            current.RegisterCallback(geometryChanged);
            if (current is ScrollView scroll)
            {
                scrollOwners.Add(scroll);
                scroll.horizontalScroller.valueChanged += scrolled;
                scroll.verticalScroller.valueChanged += scrolled;
            }
            if (current == root)
                break;
        }
        Target.RegisterCallback(detached);
        if (root != Target)
            root.RegisterCallback(detached);
    }

    private void UnregisterMovementTracking()
    {
        if (geometryChanged != null)
        {
            foreach (VisualElement owner in geometryOwners)
                owner?.UnregisterCallback(geometryChanged);
        }
        if (scrolled != null)
        {
            foreach (ScrollView scroll in scrollOwners)
            {
                if (scroll == null)
                    continue;
                scroll.horizontalScroller.valueChanged -= scrolled;
                scroll.verticalScroller.valueChanged -= scrolled;
            }
        }
        if (detached != null)
        {
            Target?.UnregisterCallback(detached);
            if (root != Target)
                root?.UnregisterCallback(detached);
        }
        geometryOwners.Clear();
        scrollOwners.Clear();
        geometryChanged = null;
        detached = null;
        scrolled = null;
    }

    private void CancelFromDetach()
    {
        VisualElement staleCue = cue;
        VisualElement staleRoot = root;
        Action<Action, VisualElement, int> deferRemoval = deferAction;
        if (staleCue != null)
            staleCue.style.display = DisplayStyle.None;
        // UI Toolkit can dispatch DetachFromPanelEvent while Hierarchy.Clear is
        // between removing a child index and nulling its parent. Do not mutate
        // that hierarchy reentrantly; a finite deferred cleanup handles a lone
        // target detach, while an owning clear removes the hidden cue itself.
        Cancel(false);
        if (staleCue?.parent != null && deferRemoval != null)
        {
            deferRemoval(() =>
            {
                if (staleCue.parent != null)
                    staleCue.RemoveFromHierarchy();
            }, staleRoot, 0);
        }
    }

    private bool RefreshBounds()
    {
        if (root == null || Target == null || cue == null || cue.parent != root ||
            root.panel == null || Target.panel != root.panel)
        {
            Cancel();
            return false;
        }
        for (VisualElement current = Target; current != null; current = current.parent)
        {
            if (current.resolvedStyle.display == DisplayStyle.None ||
                current.resolvedStyle.visibility == Visibility.Hidden)
            {
                Cancel();
                return false;
            }
            if (current == root)
                break;
        }
        Rect targetBounds = Target.worldBound;
        if (!HasUsableBounds(targetBounds))
        {
            Cancel();
            return false;
        }
        Rect visibleBounds = targetBounds;
        for (int index = 0; index < scrollOwners.Count; index++)
        {
            ScrollView scroll = scrollOwners[index];
            VisualElement viewport = scroll?.contentViewport;
            Rect viewportBounds = viewport?.worldBound ?? default;
            if (viewport == null || viewport.panel != root.panel ||
                !HasUsableBounds(viewportBounds) ||
                !TryIntersect(visibleBounds, viewportBounds, out visibleBounds))
            {
                Cancel();
                return false;
            }
        }
        Vector2 topLeft = root.WorldToLocal(targetBounds.position);
        cue.style.left = topLeft.x - 3f;
        cue.style.top = topLeft.y - 3f;
        cue.style.width = targetBounds.width + 6f;
        cue.style.height = targetBounds.height + 6f;
        return true;
    }

    private static bool HasUsableBounds(Rect bounds)
    {
        return !float.IsNaN(bounds.x) && !float.IsInfinity(bounds.x) &&
            !float.IsNaN(bounds.y) && !float.IsInfinity(bounds.y) &&
            !float.IsNaN(bounds.width) && !float.IsInfinity(bounds.width) &&
            !float.IsNaN(bounds.height) && !float.IsInfinity(bounds.height) &&
            bounds.width > 0f && bounds.height > 0f;
    }

    private static bool TryIntersect(Rect left, Rect right, out Rect intersection)
    {
        float xMin = Mathf.Max(left.xMin, right.xMin);
        float yMin = Mathf.Max(left.yMin, right.yMin);
        float xMax = Mathf.Min(left.xMax, right.xMax);
        float yMax = Mathf.Min(left.yMax, right.yMax);
        intersection = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return xMax > xMin && yMax > yMin;
    }

    private void SetOpacity(int token, float opacity)
    {
        if (token == generation && cue != null)
            cue.style.opacity = opacity;
    }

    private void Clear(int token)
    {
        if (token == generation)
            Cancel();
    }
}

/// <summary>
/// A two-pane workspace whose child trees remain mounted for its lifetime.
/// Collapse, reveal, orientation, and size changes happen in place so fields
/// do not receive false panel detach/attach events and lose focus, hover state,
/// or in-progress UI Toolkit transitions.
/// </summary>
public sealed class ThreadlightRetainedSplitView : VisualElement
{
    private readonly TwoPaneSplitView splitView;
    private bool? secondaryVisible;

    public ThreadlightRetainedSplitView(
        VisualElement primary,
        VisualElement secondary,
        float secondaryDimension,
        TwoPaneSplitViewOrientation orientation)
    {
        if (primary == null)
            throw new ArgumentNullException(nameof(primary));
        if (secondary == null)
            throw new ArgumentNullException(nameof(secondary));
        style.flexGrow = 1f;
        style.minWidth = 0f;
        style.minHeight = 0f;
        splitView = new TwoPaneSplitView(
            1, secondaryDimension, orientation);
        splitView.AddToClassList("threadlight-retained-split-view__split");
        splitView.style.flexGrow = 1f;
        splitView.style.minWidth = 0f;
        splitView.style.minHeight = 0f;
        splitView.Add(primary);
        splitView.Add(secondary);
        hierarchy.Add(splitView);
        Orientation = orientation;
        RegisterCallback<AttachToPanelEvent>(evt =>
        {
            if (evt.target == this && secondaryVisible.HasValue)
                ApplySecondaryVisibility();
        });
    }

    public bool SecondaryVisible => secondaryVisible ?? true;
    public TwoPaneSplitViewOrientation Orientation { get; private set; }

    public void ConfigureSecondary(
        bool visible,
        float dimension,
        TwoPaneSplitViewOrientation nextOrientation)
    {
        splitView.orientation = nextOrientation;
        splitView.fixedPaneInitialDimension = Mathf.Max(0f, dimension);
        Orientation = nextOrientation;
        // The native splitter can reset its internal collapsed state during a
        // domain reload while this retained wrapper still remembers the same
        // logical value. Always enforce the requested native state; both
        // CollapseChild and UnCollapse are safe to repeat.
        secondaryVisible = visible;
        ApplySecondaryVisibility();
    }

    private void ApplySecondaryVisibility()
    {
        if (!secondaryVisible.HasValue)
            return;
        if (secondaryVisible.Value)
            splitView.UnCollapse();
        else
            splitView.CollapseChild(1);
    }
}
}
