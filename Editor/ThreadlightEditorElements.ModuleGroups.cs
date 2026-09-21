namespace Threadlight.EditorUI {
using System;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
public static partial class ThreadlightEditorElements {
    private sealed class ModuleGroupPalette { public Color Accent, Fill; }
    private static readonly ConditionalWeakTable<VisualElement, ModuleGroupPalette> ModuleGroupPalettes =
        new ConditionalWeakTable<VisualElement, ModuleGroupPalette>();
    private const double DisclosureAnimationSeconds = 0.18d;
    private const int DisclosureMeasureAttempts = 3;
    private sealed class ExpansionAnimationState {
        public EventCallback<GeometryChangedEvent> PendingMeasure;
        public EditorApplication.CallbackFunction PendingProbe;
        public EditorApplication.CallbackFunction PendingStart;
        public EditorApplication.CallbackFunction PendingFinalize;
        public double FinalizeAt;
        public bool Expanded;
        public float NaturalHeight;
        public bool MeasuringOutsideLayout;
        public StyleEnum<Position> SavedPosition;
        public StyleLength SavedWidth;
    }
    private static readonly ConditionalWeakTable<VisualElement, ExpansionAnimationState> ExpansionAnimations =
        new ConditionalWeakTable<VisualElement, ExpansionAnimationState>();
    public static void StyleModuleGroupSurface(VisualElement group, Color accent, Color fill) {
        if (group == null) return;
        ApplySharedStyles(group);
        group.AddToClassList("threadlight-module-group");
        group.AddToClassList("threadlight-bordered-surface");
        group.AddToClassList("threadlight-level--feature");
        ModuleGroupPalette palette = ModuleGroupPalettes.GetValue(group, _ => new ModuleGroupPalette());
        palette.Accent = accent;
        palette.Fill = fill;
        group.style.backgroundColor = ModuleGroupBackgroundColor(group, fill);
        SetBorderColor(group, ModuleGroupBorderColor(group, accent));
        RegisterPointerLight(group, accent, 122f, .38f);
    }
    public static VisualElement BuildModuleGroupHeader(VisualElement header, string title, string kind,
        Color accent, bool expanded, out Label expansionIndicator, out Label titleLabel) {
        if (header == null) throw new ArgumentNullException(nameof(header));
        header.Clear();
        ApplySharedStyles(header);
        header.AddToClassList("threadlight-module-header");
        expansionIndicator = Label(expanded ? "−" : "+", "threadlight-module-indicator", accent);
        header.Add(expansionIndicator);
        titleLabel = Label(title, "threadlight-module-title", accent);
        header.Add(titleLabel);
        if (!string.IsNullOrWhiteSpace(kind) &&
            !string.Equals(kind.Trim(), "SECTION", StringComparison.OrdinalIgnoreCase)) {
            Label kindLabel = Label(kind.Trim().ToUpperInvariant(),
                "threadlight-module-kind", GroupColor(accent, .88f));
            kindLabel.style.backgroundColor = GroupColor(accent, .10f);
            header.Add(kindLabel);
        }
        VisualElement accessories = new VisualElement();
        accessories.AddToClassList("threadlight-module-accessories");
        header.Add(accessories);
        SetModuleGroupExpansion(header, expansionIndicator, accent, expanded);
        return accessories;
    }
    private static Label Label(string text, string className, Color color) {
        Label label = new Label(text ?? string.Empty);
        label.AddToClassList(className);
        label.style.color = color;
        return label;
    }
    public static void SetModuleGroupExpansion(VisualElement header, Label indicator, Color accent, bool expanded) {
        if (indicator != null) indicator.text = expanded ? "−" : "+";
        if (header == null) return;
        header.style.marginBottom = expanded ? 5f : 0f;
        header.style.borderBottomWidth = expanded ? 1f : 0f;
        header.style.borderBottomColor = GroupColor(accent, expanded ? .28f : 0f);
    }
    private static int ModuleGroupDepth(VisualElement group, ref Color accent, ref Color fill) {
        int depth = 0;
        for (VisualElement parent = group?.parent; parent != null; parent = parent.parent) {
            if (!parent.ClassListContains("threadlight-module-group")) continue;
            depth++;
            if (ModuleGroupPalettes.TryGetValue(parent, out ModuleGroupPalette palette)) {
                accent = palette.Accent;
                fill = palette.Fill;
            }
        }
        return depth;
    }
    /// <summary>Preserve the parent hue and soften frames as groups become deeper.</summary>
    public static Color ModuleGroupBorderColor(VisualElement group, Color accent, bool active = false) {
        Color fill = Color.clear;
        int depth = ModuleGroupDepth(group, ref accent, ref fill);
        return GroupColor(accent, active ? .72f : depth == 0 ? .56f : depth == 1 ? .30f : .14f);
    }
    /// <summary>Give nested surfaces stable, progressively lighter warm-neutral layers.</summary>
    public static Color ModuleGroupBackgroundColor(VisualElement group, Color fallback) {
        Color accent = Color.white;
        Color fill = group != null && ModuleGroupPalettes.TryGetValue(group, out ModuleGroupPalette palette)
            ? palette.Fill : fallback;
        // Resolve the original outer palette in one pass, without stacking
        // cached overlays or allocating per-frame layout/animation work.
        int depth = ModuleGroupDepth(group, ref accent, ref fill);
        float opacity = depth == 0 ? .56f : depth == 1 ? .24f : depth == 2 ? .18f : .12f / (depth - 2);
        float lift = depth == 0 ? 0f : depth == 1 ? .08f : depth == 2 ? .15f : .22f;
        Color shifted = Color.Lerp(fill, new Color(1f, .94f, .86f, 1f), lift);
        shifted.a = Mathf.Min(fill.a, opacity);
        return shifted;
    }
    public static void SetModuleGroupInteraction(VisualElement group, VisualElement header, Color accent,
        bool hovered, bool focused) {
        if (group != null) SetBorderColor(group, ModuleGroupBorderColor(group, accent, hovered || focused));
        if (header != null) header.style.backgroundColor = Color.clear;
    }
    public static void RegisterModuleGroupHeaderInteraction(VisualElement group, VisualElement header,
        Color accent, Action toggled) {
        if (header == null) return;
        header.style.backgroundColor = Color.clear;
        bool pointerFocus = false;
        Action refresh = null;
        // Pointer focus still supports keyboard input, but should not leave a
        // resting folder looking hovered after the pointer moves away.
        header.RegisterCallback<PointerDownEvent>(_ => {
            pointerFocus = true;
            refresh?.Invoke();
        }, TrickleDown.TrickleDown);
        RegisterInteractionState(header, (hovered, focused) =>
            SetModuleGroupInteraction(group, header, accent, hovered, focused && !pointerFocus), out refresh);
        header.RegisterCallback<FocusOutEvent>(evt => {
            if (evt.target != header) return;
            pointerFocus = false;
            refresh();
        });
        header.RegisterCallback<KeyDownEvent>(evt => {
            if (evt.target != header) return;
            pointerFocus = false;
            refresh();
        });
        header.RegisterCallback<AttachToPanelEvent>(evt => {
            if (evt.target == header) {
                if (group != null)
                    group.style.backgroundColor = ModuleGroupBackgroundColor(group, group.style.backgroundColor.value);
                refresh();
            }
        });
        header.RegisterCallback<KeyDownEvent>(evt => {
            if (evt.target != header || evt.keyCode != KeyCode.Return &&
                evt.keyCode != KeyCode.KeypadEnter && evt.keyCode != KeyCode.Space) return;
            toggled?.Invoke(); evt.StopPropagation(); evt.PreventDefault();
        });
    }
    /// <summary>Shared pointer, keyboard and accessory-button behavior for disclosure headers.</summary>
    public static void RegisterDisclosureHeader(VisualElement header, Action toggle,
        VisualElement surface = null, Color? accent = null, Func<VisualElement, bool> ignoreClick = null) {
        if (header == null) return;
        header.focusable = true;
        header.tabIndex = 0;
        header.RegisterCallback<PointerUpEvent>(evt => {
            VisualElement target = evt.target as VisualElement;
            if (evt.button != 0 || ignoreClick?.Invoke(target) == true) return;
            for (VisualElement current = target; current != null && current != header; current = current.parent)
                if (current is Button) return;
            toggle?.Invoke();
            evt.StopPropagation();
        });
        RegisterModuleGroupHeaderInteraction(surface, header,
            accent ?? ThreadlightEditorTheme.Accent, toggle);
    }
    public static void StyleModuleGroupBody(VisualElement body) {
        if (body == null) return;
        StyleExpandableBody(body);
        body.AddToClassList("threadlight-module-body");
        body.style.backgroundColor = Color.clear;
    }
    public static void SetModuleGroupBodyExpansion(VisualElement body, bool expanded, bool animate) {
        if (body == null) return;
        StyleExpandableBody(body);
        ExpansionAnimationState state = ExpansionAnimations.GetValue(body, element => {
            ExpansionAnimationState created = new ExpansionAnimationState();
            element.RegisterCallback<DetachFromPanelEvent>(evt => {
                if (evt.target == element)
                    CompleteExpansion(element, created);
            });
            return created;
        });
        bool wasMeasuring = state.MeasuringOutsideLayout;
        bool reversing = !wasMeasuring && state.Expanded != expanded &&
            (state.PendingStart != null || state.PendingFinalize != null);
        CancelPendingExpansion(body, state);
        state.Expanded = expanded;
        if (!animate || ThreadlightEditorPreferences.ReducedMotion || body.panel == null) {
            ApplyExpansionState(body, expanded);
            return;
        }
        if (expanded && reversing && UsableExpansionHeight(state.NaturalHeight)) {
            // Keep the current interpolated height, opacity and translation on reversal.
            body.style.maxHeight = state.NaturalHeight;
            body.style.opacity = 1f;
            body.style.translate = new Translate(0, 0, 0);
            ScheduleExpansionFinalize(body, state);
        }
        else if (expanded) BeginOpening(body, state);
        else if (wasMeasuring) ApplyExpansionState(body, false);
        else BeginClosing(body, state, reversing);
    }
    private static void StyleExpandableBody(VisualElement body) {
        ApplySharedStyles(body);
        body.AddToClassList("threadlight-expandable-body");
    }
    private static void BeginOpening(VisualElement body, ExpansionAnimationState state) {
        float width = body.parent != null ? body.parent.contentRect.width : body.resolvedStyle.width;
        if (!UsableExpansionHeight(width)) {
            ApplyExpansionState(body, true);
            return;
        }
        // Measure in place, but outside flow: hiding only the opacity still lets
        // the full-height body push every subsequent folder down for one frame.
        // Preserve inline styles because this helper also serves non-card bodies.
        state.SavedPosition = body.style.position;
        state.SavedWidth = body.style.width;
        state.MeasuringOutsideLayout = true;
        body.style.position = Position.Absolute;
        body.style.width = width;
        body.style.display = DisplayStyle.Flex;
        body.style.maxHeight = StyleKeyword.Null;
        body.style.overflow = Overflow.Hidden;
        body.style.opacity = 0f;
        body.style.translate = new Translate(0, -6, 0);
        EventCallback<GeometryChangedEvent> measure = null;
        measure = evt => {
            if (state.PendingMeasure != measure || !state.Expanded) return;
            float height = evt.newRect.height;
            if (UsableExpansionHeight(height)) StartOpening(body, state, height);
        };
        state.PendingMeasure = measure;
        body.RegisterCallback(measure);
        int attempts = 0;
        EditorApplication.CallbackFunction probe = null;
        probe = () => {
            if (state.PendingProbe != probe || !state.Expanded) return;
            attempts++;
            // Give Yoga a layout pass; the first update can still contain the
            // previous (possibly partially collapsed) resolved height.
            if (attempts == 1) return;
            float height = body.resolvedStyle.height;
            if (UsableExpansionHeight(height)) StartOpening(body, state, height);
            else if (attempts >= DisclosureMeasureAttempts) CompleteExpansion(body, state);
        };
        state.PendingProbe = probe;
        EditorApplication.update += probe;
    }
    private static void StartOpening(VisualElement body, ExpansionAnimationState state, float targetHeight) {
        state.NaturalHeight = targetHeight;
        // Clamp before returning to normal flow, so no layout sees full height.
        body.style.maxHeight = 0f;
        CancelPendingMeasurement(body, state);
        QueueExpansionStart(body, state, () => {
            body.style.maxHeight = targetHeight;
            body.style.opacity = 1f;
            body.style.translate = new Translate(0, 0, 0);
        });
    }
    private static void BeginClosing(VisualElement body, ExpansionAnimationState state, bool reversing) {
        float height = body.resolvedStyle.height;
        if (!UsableExpansionHeight(height)) {
            ApplyExpansionState(body, false);
            return;
        }
        if (!reversing) {
            state.NaturalHeight = height;
            body.style.maxHeight = height;
        }
        body.style.display = DisplayStyle.Flex;
        body.style.overflow = Overflow.Hidden;
        QueueExpansionStart(body, state, () => {
            body.style.maxHeight = 0f;
            body.style.opacity = 0f;
            body.style.translate = new Translate(0, -6, 0);
        });
    }
    private static void QueueExpansionStart(VisualElement body, ExpansionAnimationState state, Action start) {
        EditorApplication.CallbackFunction pending = null;
        pending = () => {
            if (state.PendingStart != pending) return;
            state.PendingStart = null;
            if (body == null || body.panel == null) {
                CompleteExpansion(body, state);
                return;
            }
            start?.Invoke();
            ScheduleExpansionFinalize(body, state);
        };
        state.PendingStart = pending;
        EditorApplication.delayCall += pending;
    }
    private static void ScheduleExpansionFinalize(VisualElement body, ExpansionAnimationState state) {
        state.FinalizeAt = EditorApplication.timeSinceStartup + DisclosureAnimationSeconds;
        EditorApplication.CallbackFunction finalize = null;
        finalize = () => {
            if (state.PendingFinalize != finalize) return;
            if (body == null || body.panel == null ||
                EditorApplication.timeSinceStartup >= state.FinalizeAt)
                CompleteExpansion(body, state);
        };
        state.PendingFinalize = finalize;
        EditorApplication.update += finalize;
    }
    private static void CompleteExpansion(VisualElement body, ExpansionAnimationState state) {
        if (body == null || state == null) return;
        CancelPendingExpansion(body, state);
        ApplyExpansionState(body, state.Expanded);
    }
    private static void CancelPendingExpansion(VisualElement body, ExpansionAnimationState state) {
        CancelPendingMeasurement(body, state);
        if (state.PendingStart != null) {
            EditorApplication.delayCall -= state.PendingStart;
            state.PendingStart = null;
        }
        if (state.PendingFinalize != null) {
            EditorApplication.update -= state.PendingFinalize;
            state.PendingFinalize = null;
        }
    }
    private static void CancelPendingMeasurement(VisualElement body, ExpansionAnimationState state) {
        if (state.PendingMeasure != null) {
            body?.UnregisterCallback(state.PendingMeasure);
            state.PendingMeasure = null;
        }
        if (state.PendingProbe != null) {
            EditorApplication.update -= state.PendingProbe;
            state.PendingProbe = null;
        }
        if (state.MeasuringOutsideLayout) {
            state.MeasuringOutsideLayout = false;
            if (body != null) {
                body.style.position = state.SavedPosition;
                body.style.width = state.SavedWidth;
            }
        }
    }
    private static void ApplyExpansionState(VisualElement body, bool expanded) {
        body.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
        body.style.maxHeight = expanded ? StyleKeyword.Null : 0f;
        body.style.overflow = expanded ? Overflow.Visible : Overflow.Hidden;
        body.style.opacity = expanded ? 1f : 0f;
        body.style.translate = expanded ? new Translate(0, 0, 0) : new Translate(0, -6, 0);
    }
    private static bool UsableExpansionHeight(float height) =>
        !float.IsNaN(height) && !float.IsInfinity(height) && height > 0f;
    private static Color GroupColor(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
}

/// <summary>
/// Shared expandable card shell for editor tools. Feature surfaces supply
/// their own validation, actions, colors, and state persistence while focus,
/// keyboard input, pointer feedback, and expansion remain consistent.
/// </summary>
public class ThreadlightDisclosureElement : VisualElement {
    protected readonly VisualElement Body;
    private readonly VisualElement reveal;
    protected readonly VisualElement Header;
    protected bool Expanded;
    private Action<bool> presentExpansion;
    private Action<bool> expansionChanged;
    private bool configured;

    public VisualElement Content => Body;
    public VisualElement HeaderElement => Header;
    public bool IsExpanded => Expanded;
    public override VisualElement contentContainer => Body ?? this;

    public ThreadlightDisclosureElement() {
        Header = new VisualElement();
        reveal = new VisualElement();
        Body = new VisualElement();
        hierarchy.Add(Header);
        hierarchy.Add(reveal);
        reveal.Add(Body);
    }

    /// <summary>Bind presentation and persistence once; neither callback owns animation or input.</summary>
    public void ConfigureDisclosure(bool expanded, Color accent,
        Action<bool> presentation, Action<bool> changed = null) {
        if (configured) throw new InvalidOperationException("A disclosure is already configured.");
        configured = true;
        presentExpansion = presentation;
        expansionChanged = changed;
        ApplyExpanded(expanded, false);
        ThreadlightEditorElements.RegisterDisclosureHeader(Header, Toggle, this, accent);
    }

    protected virtual void Toggle() {
        SetDisclosureExpanded(!Expanded);
    }

    /// <summary>Reveal or collapse this editor-only disclosure through its normal presentation and persistence path.</summary>
    public void SetDisclosureExpanded(bool value) {
        SetDisclosureExpanded(value, true);
    }

    /// <summary>Allows navigation to reveal before measuring without starting a transitional layout.</summary>
    public void SetDisclosureExpanded(bool value, bool animate) {
        if (Expanded == value) return;
        ApplyExpanded(value, animate);
        expansionChanged?.Invoke(Expanded);
    }

    protected void SetExpanded(bool value) => ApplyExpanded(value, true);

    private void ApplyExpanded(bool value, bool animate) {
        Expanded = value;
        presentExpansion?.Invoke(value);
        ThreadlightEditorElements.SetModuleGroupBodyExpansion(reveal, value, animate);
    }
}

/// <summary>Standard folder presentation over the same disclosure used by custom Builder headers.</summary>
public class ThreadlightDisclosureCard : ThreadlightDisclosureElement {
    protected readonly VisualElement Accessories;
    protected readonly Label Arrow;
    protected readonly Label Heading;
    protected readonly Color Accent;

    public ThreadlightDisclosureCard(
        string title,
        string description,
        Color accent,
        Color fill,
        bool expanded = true,
        string kind = "SECTION",
        string descriptionClassName = null,
        Action<bool> expansionChanged = null) {
        Accent = accent;
        ThreadlightEditorElements.StyleModuleGroupSurface(this, accent, fill);
        Accessories = ThreadlightEditorElements.BuildModuleGroupHeader(
            Header,
            title,
            kind,
            accent,
            expanded,
            out Label arrow,
            out Label heading);
        Arrow = arrow;
        Heading = heading;
        ThreadlightEditorElements.StyleModuleGroupBody(Body);

        if (!string.IsNullOrWhiteSpace(description)) {
            Label copy = ThreadlightEditorElements.CreateDescription(description.Trim(), descriptionClassName);
            Body.Add(copy);
        }

        ConfigureDisclosure(expanded, accent, value =>
            ThreadlightEditorElements.SetModuleGroupExpansion(Header, Arrow, Accent, value),
            expansionChanged);
    }
}
}
