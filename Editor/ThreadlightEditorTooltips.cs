namespace Threadlight.EditorUI {
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
public static class ThreadlightEditorTooltips {
    public const long HoverDelayMilliseconds = 250;
    public const float TooltipMargin = 6f, TooltipOffsetX = 14f, TooltipOffsetY = 18f;
    public const float MinimumTooltipWidth = 112f, PreferredMaximumTooltipWidth = 360f;
    public const float TooltipPaddingHorizontal = 9f, TooltipPaddingVertical = 7f, TooltipCornerRadius = 5f;
    public static string Get(string label, SerializedProperty property = null) =>
        property != null && !string.IsNullOrWhiteSpace(property.tooltip) ? property.tooltip : string.Empty;
    public static void SuppressNativeTooltip(VisualElement owner) {
        if (owner == null) return;
        Suppress(owner);
        owner.Query<VisualElement>().ForEach(Suppress);
    }
    private static void Suppress(VisualElement element) {
        element.tooltip = null;
        if (element.ClassListContains("prop-tools-native-tooltip-suppressed")) return;
        element.AddToClassList("prop-tools-native-tooltip-suppressed");
        element.RegisterCallback<TooltipEvent>(evt =>
        { evt.tooltip = string.Empty; evt.StopImmediatePropagation(); evt.PreventDefault(); }, TrickleDown.TrickleDown);
    }
}
public sealed class ThreadlightEditorTooltipLayer : IDisposable {
    private readonly VisualElement host, tooltip;
    private readonly Label title, body;
    private readonly VisualElement divider;
    private EditorApplication.CallbackFunction pending;
    private EditorApplication.CallbackFunction refreshPending;
    private VisualElement owner, pendingOwner;
    private VisualElement refreshPendingOwner;
    private Vector2 pointer;
    private int generation;
    public ThreadlightEditorTooltipLayer(VisualElement hostElement) {
        host = hostElement ?? throw new ArgumentNullException(nameof(hostElement));
        tooltip = new VisualElement { pickingMode = PickingMode.Ignore };
        tooltip.style.position = UnityEngine.UIElements.Position.Absolute;
        tooltip.style.visibility = Visibility.Hidden;
        tooltip.style.minWidth = ThreadlightEditorTooltips.MinimumTooltipWidth;
        tooltip.style.maxWidth = ThreadlightEditorTooltips.PreferredMaximumTooltipWidth;
        tooltip.style.paddingLeft = tooltip.style.paddingRight = ThreadlightEditorTooltips.TooltipPaddingHorizontal;
        tooltip.style.paddingTop = tooltip.style.paddingBottom = ThreadlightEditorTooltips.TooltipPaddingVertical;
        tooltip.style.backgroundColor = ThreadlightEditorTheme.Panel;
        ThreadlightEditorElements.SetBorderColor(tooltip, ThreadlightEditorTheme.BorderStrong);
        tooltip.style.borderLeftWidth = tooltip.style.borderRightWidth = 1;
        tooltip.style.borderTopWidth = tooltip.style.borderBottomWidth = 1;
        ThreadlightEditorElements.SetRadius(tooltip, ThreadlightEditorTooltips.TooltipCornerRadius);
        title = new Label { pickingMode = PickingMode.Ignore };
        title.style.color = ThreadlightEditorTheme.Text; title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.fontSize = 12;
        title.style.whiteSpace = WhiteSpace.Normal;
        title.style.marginLeft = title.style.marginRight = 0;
        title.style.marginTop = title.style.marginBottom = 0;
        divider = new VisualElement { pickingMode = PickingMode.Ignore };
        divider.style.height = 1;
        divider.style.flexShrink = 0;
        divider.style.marginTop = divider.style.marginBottom = 5;
        divider.style.backgroundColor =
            ThreadlightEditorTheme.Palette(ThreadlightEditorTone.Standard).Accent;
        body = new Label { pickingMode = PickingMode.Ignore };
        body.style.color = ThreadlightEditorTheme.Text; body.style.whiteSpace = WhiteSpace.Normal;
        body.style.fontSize = 11;
        body.style.marginLeft = body.style.marginRight = 0;
        body.style.marginTop = body.style.marginBottom = 0;
        tooltip.Add(title); tooltip.Add(divider); tooltip.Add(body); host.Add(tooltip);
        tooltip.RegisterCallback<GeometryChangedEvent>(_ => Position());
        host.RegisterCallback<PointerLeaveEvent>(evt => {
            if (evt.target == host) Hide();
        });
        host.RegisterCallback<BlurEvent>(_ => Hide());
        host.RegisterCallback<DetachFromPanelEvent>(evt => {
            if (evt.target == host) Hide();
        });
    }
    public T Register<T>(T element, string heading, string text) where T : VisualElement {
        if (element == null || string.IsNullOrWhiteSpace(text)) return element;
        return RegisterCore(element, () => heading, () => text, null, false);
    }
    public T Register<T>(T element, Func<string> heading, Func<string> text) where T : VisualElement {
        return RegisterCore(element, heading, text, null, true);
    }
    public T RegisterFullTextWhenElided<T>(T element, string heading) where T : TextElement {
        if (element == null) return element;
        element.displayTooltipWhenElided = false;
        return RegisterCore(element, () => heading, () => element.text,
            () => IsFullTextElided(element), true);
    }
    private static bool IsFullTextElided(TextElement element) {
        if (element == null || string.IsNullOrEmpty(element.text)) return false;
        if (element.resolvedStyle.whiteSpace == WhiteSpace.Normal) return false;
        if (element.isElided) return true;
        float availableWidth = element.contentRect.width;
        if (element.panel == null || !Valid(availableWidth)) return false;
        Vector2 fullSize = element.MeasureTextSize(
            element.text, 0f, VisualElement.MeasureMode.Undefined,
            0f, VisualElement.MeasureMode.Undefined);
        return Valid(fullSize.x) && fullSize.x > availableWidth + 0.5f;
    }
    private T RegisterCore<T>(T element, Func<string> heading, Func<string> text,
        Func<bool> canShow, bool refreshFromRender) where T : VisualElement {
        if (element == null || text == null) return element;
        ThreadlightEditorTooltips.SuppressNativeTooltip(element);
        bool pointerWithin = false;
        bool focusWithin = false;
        bool keyboardFocus = false;
        bool CanShow() => canShow == null || canShow();
        Func<string> eligibleText = () => CanShow() ? text() : null;
        void TrySchedule(bool fromFocus) {
            if (!CanShow()) { Cancel(element); return; }
            if (fromFocus) pointer = FocusAnchor(element);
            Schedule(element, heading, eligibleText);
        }
        void ScheduleFromPointer() => TrySchedule(false);
        void ScheduleFromFocus() {
            TrySchedule(true);
        }
        element.RegisterCallback<PointerEnterEvent>(evt =>
        { pointerWithin = true; pointer = host.WorldToLocal(evt.position); ScheduleFromPointer(); });
        element.RegisterCallback<PointerMoveEvent>(evt =>
        {
            pointer = host.WorldToLocal(evt.position);
            if (!CanShow()) Cancel(element);
            else if (owner == element)
                RefreshVisibleText(element, heading, eligibleText, canShow);
            else if (pointerWithin) ScheduleFromPointer();
        });
        element.RegisterCallback<PointerDownEvent>(_ => {
            keyboardFocus = false;
            Cancel(element);
        });
        element.RegisterCallback<PointerLeaveEvent>(_ => {
            pointerWithin = false;
            if (focusWithin && keyboardFocus) {
                pointer = FocusAnchor(element);
                if (owner == element) Position(); else ScheduleFromFocus();
            } else Cancel(element);
        });
        element.RegisterCallback<FocusInEvent>(_ => {
            if (focusWithin) return;
            focusWithin = true;
            keyboardFocus = !pointerWithin;
            if (keyboardFocus) ScheduleFromFocus();
        });
        element.RegisterCallback<FocusOutEvent>(evt => {
            if (evt.relatedTarget is VisualElement next && element.Contains(next)) return;
            focusWithin = keyboardFocus = false;
            if (!pointerWithin) Cancel(element);
        });
        element.RegisterCallback<GeometryChangedEvent>(_ => {
            if (!CanShow()) { Cancel(element); return; }
            if (owner == element)
                RefreshVisibleText(element, heading, eligibleText, canShow);
            else if (pointerWithin) ScheduleFromPointer();
            else if (focusWithin && keyboardFocus) ScheduleFromFocus();
        });
        if (refreshFromRender)
            element.generateVisualContent += _ =>
                QueueVisibleTextRefresh(element, heading, eligibleText, canShow);
        element.RegisterCallback<DetachFromPanelEvent>(evt => {
            if (evt.target != element) return;
            pointerWithin = focusWithin = keyboardFocus = false;
            Cancel(element);
        });
        return element;
    }
    public void Hide() {
        CancelPending();
        CancelVisibleTextRefresh();
        owner = null;
        tooltip.style.visibility = Visibility.Hidden;
    }
    public void Dispose() { Hide(); tooltip.RemoveFromHierarchy(); }
    private void QueueVisibleTextRefresh(VisualElement element, Func<string> heading,
        Func<string> text, Func<bool> canShow) {
        if (owner != element && pendingOwner != element) return;
        if (refreshPendingOwner == element) return;
        CancelVisibleTextRefresh();
        refreshPendingOwner = element;
        refreshPending = () => {
            EditorApplication.delayCall -= refreshPending;
            refreshPending = null;
            refreshPendingOwner = null;
            RefreshVisibleText(element, heading, text, canShow);
        };
        EditorApplication.delayCall += refreshPending;
    }
    private void RefreshVisibleText(VisualElement element, Func<string> heading,
        Func<string> text, Func<bool> canShow) {
        if (owner != element) {
            if (pendingOwner == element && canShow != null && !canShow())
                CancelPending();
            return;
        }
        if (canShow != null && !canShow()) { Cancel(element); return; }
        string resolvedText = text?.Invoke()?.Trim();
        if (string.IsNullOrWhiteSpace(resolvedText)) { Cancel(element); return; }
        string resolvedHeading = heading?.Invoke()?.Trim() ?? string.Empty;
        if (title.text != resolvedHeading) title.text = resolvedHeading;
        bool titled = !string.IsNullOrWhiteSpace(resolvedHeading);
        title.style.display = divider.style.display = titled
            ? DisplayStyle.Flex : DisplayStyle.None;
        if (body.text != resolvedText) body.text = resolvedText;
        Position();
    }
    private void Schedule(VisualElement element, Func<string> heading, Func<string> text) {
        CancelPending();
        pendingOwner = element;
        int token = ++generation;
        double at = EditorApplication.timeSinceStartup + ThreadlightEditorTooltips.HoverDelayMilliseconds / 1000d;
        pending = () => {
            if (token != generation || EditorApplication.timeSinceStartup < at) return;
            CancelPending();
            if (element.panel == null || host.panel == null || element.panel != host.panel) return;
            string resolvedText = text?.Invoke();
            if (string.IsNullOrWhiteSpace(resolvedText)) return;
            owner = element;
            title.text = heading?.Invoke()?.Trim() ?? string.Empty;
            bool titled = !string.IsNullOrWhiteSpace(title.text);
            title.style.display = divider.style.display = titled ? DisplayStyle.Flex : DisplayStyle.None;
            body.text = resolvedText.Trim();
            tooltip.style.visibility = Visibility.Visible;
            tooltip.BringToFront();
            Position();
        };
        EditorApplication.update += pending;
    }
    private void Cancel(VisualElement element) {
        if (owner == element) Hide();
        if (pendingOwner == element) CancelPending();
        if (refreshPendingOwner == element) CancelVisibleTextRefresh();
    }
    private void CancelVisibleTextRefresh() {
        if (refreshPending != null)
            EditorApplication.delayCall -= refreshPending;
        refreshPending = null;
        refreshPendingOwner = null;
    }
    private void CancelPending() {
        generation++;
        if (pending == null) return;
        EditorApplication.update -= pending;
        pending = null;
        pendingOwner = null;
    }
    private void Position() {
        if (owner == null || host.panel == null || tooltip.panel == null) return;
        float margin = ThreadlightEditorTooltips.TooltipMargin;
        float availableWidth = Mathf.Max(1, host.resolvedStyle.width - margin * 2);
        tooltip.style.minWidth = Mathf.Min(ThreadlightEditorTooltips.MinimumTooltipWidth, availableWidth);
        tooltip.style.maxWidth = Mathf.Min(ThreadlightEditorTooltips.PreferredMaximumTooltipWidth, availableWidth);
        float width = Valid(tooltip.resolvedStyle.width) ? tooltip.resolvedStyle.width : ThreadlightEditorTooltips.MinimumTooltipWidth;
        float height = Valid(tooltip.resolvedStyle.height) ? tooltip.resolvedStyle.height : 1;
        float left = pointer.x + ThreadlightEditorTooltips.TooltipOffsetX;
        float top = pointer.y + ThreadlightEditorTooltips.TooltipOffsetY;
        if (top + height > host.resolvedStyle.height - margin) top = pointer.y - height - ThreadlightEditorTooltips.TooltipOffsetY;
        tooltip.style.left = Mathf.Clamp(left, margin, Mathf.Max(margin, host.resolvedStyle.width - width - margin));
        tooltip.style.top = Mathf.Clamp(top, margin, Mathf.Max(margin, host.resolvedStyle.height - height - margin));
    }
    private Vector2 FocusAnchor(VisualElement element) {
        Rect bounds = element.worldBound;
        return host.WorldToLocal(new Vector2(bounds.xMin, bounds.yMax));
    }
    private static bool Valid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 1;
}
}
