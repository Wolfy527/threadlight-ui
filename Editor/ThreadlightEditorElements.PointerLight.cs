namespace Threadlight.EditorUI {
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;
public static partial class ThreadlightEditorElements {
    private sealed class PointerLightState {
        public Func<Color> Tint;
        public float Diameter, Opacity;
        public VisualElement Root;
    }
    private sealed class PanelPointerLightState {
        public VisualElement ActiveOwner;
        public VisualElement Light;
    }
    private static readonly ConditionalWeakTable<VisualElement, PointerLightState> PointerLights =
        new ConditionalWeakTable<VisualElement, PointerLightState>();
    private static readonly ConditionalWeakTable<VisualElement, PanelPointerLightState> PanelPointerLights =
        new ConditionalWeakTable<VisualElement, PanelPointerLightState>();
    public static void RegisterPointerLight(VisualElement owner, Color tint,
        float diameter = 112f, float opacity = .60f) =>
        RegisterPointerLight(owner, () => tint, diameter, opacity);
    public static void RegisterPointerLight(VisualElement owner, Func<Color> tintProvider,
        float diameter = 112f, float opacity = .60f) {
        if (owner == null) return;
        owner.AddToClassList("wolfy-pointer-light-owner");
        if (!PointerLights.TryGetValue(owner, out PointerLightState state)) {
            state = new PointerLightState();
            PointerLights.Add(owner, state);
            owner.RegisterCallback<PointerEnterEvent>(evt => ShowPointerLight(owner, state, evt.target as VisualElement, evt.position));
            owner.RegisterCallback<PointerMoveEvent>(evt => ShowPointerLight(owner, state, evt.target as VisualElement, evt.position));
            owner.RegisterCallback<PointerLeaveEvent>(_ => HidePointerLight(owner));
            owner.RegisterCallback<BlurEvent>(_ => HidePointerLight(owner));
            owner.RegisterCallback<DetachFromPanelEvent>(evt => {
                if (evt.target == owner)
                    HidePointerLight(owner);
            });
        }
        state.Tint = tintProvider;
        state.Diameter = Mathf.Clamp(diameter, 64f, 160f);
        state.Opacity = Mathf.Clamp(opacity * .46f, .08f, .34f);
    }
    private static void ShowPointerLight(VisualElement owner, PointerLightState state, VisualElement target,
        Vector2 panelPosition) {
        if (ThreadlightEditorPreferences.ReducedMotion || owner.panel?.visualTree == null ||
            Suppressed(target, owner)) { HidePointerLight(owner); return; }
        VisualElement root = owner.panel.visualTree;
        state.Root = root;
        PanelPointerLightState panelState = PanelPointerLights.GetValue(root, _ => new PanelPointerLightState());
        if (panelState.Light == null) {
            panelState.Light = new VisualElement {
                name = "wolfy-pointer-light", pickingMode = PickingMode.Ignore,
                usageHints = UsageHints.DynamicTransform | UsageHints.DynamicColor
            };
            panelState.Light.style.position = Position.Absolute;
            panelState.Light.style.left = panelState.Light.style.top = 0f;
            panelState.Light.style.backgroundImage = new StyleBackground(AuroraTexture());
            root.Add(panelState.Light);
        }
        if (panelState.Light.parent != root) { panelState.Light.RemoveFromHierarchy(); root.Add(panelState.Light); }
        // A retained panel can mount a rebuilt workspace after the existing light.
        // Restore its overlay order only when necessary, without relaying out on motion.
        if (root.hierarchy.IndexOf(panelState.Light) != root.hierarchy.childCount - 1)
            panelState.Light.BringToFront();
        panelState.ActiveOwner = owner;
        Vector2 local = root.WorldToLocal(panelPosition);
        panelState.Light.style.width = panelState.Light.style.height = state.Diameter;
        // Pointer motion must not invalidate the panel's layout on every event.
        panelState.Light.transform.position = new Vector3(
            local.x - state.Diameter * .5f, local.y - state.Diameter * .5f, 0f);
        panelState.Light.style.unityBackgroundImageTintColor = state.Tint?.Invoke() ?? Color.white;
        panelState.Light.style.opacity = state.Opacity;
    }
    private static bool Suppressed(VisualElement target, VisualElement owner) {
        for (VisualElement current = target; current != null && current != owner; current = current.parent)
            if (current.ClassListContains("wolfy-pointer-light-owner") ||
                current.ClassListContains(ToggleRowClassName) || current.ClassListContains(ToggleControlClassName))
                return true;
        return false;
    }
    private static void HidePointerLight(VisualElement owner) {
        if (owner == null || !PointerLights.TryGetValue(owner, out PointerLightState ownerState)) return;
        VisualElement root = owner.panel?.visualTree ?? ownerState.Root;
        if (root == null || !PanelPointerLights.TryGetValue(root, out PanelPointerLightState state) ||
            state.ActiveOwner != owner) return;
        state.ActiveOwner = null;
        if (state.Light != null) state.Light.style.opacity = 0f;
    }
    public static void RegisterRoundedGlass(VisualElement owner, Color tint,
        float glossWidth = 126f, float opacity = .60f) =>
        RegisterPointerLight(owner, tint, glossWidth, opacity);
    public static void RegisterRoundedGlass(VisualElement owner, Func<Color> tintProvider,
        float glossWidth = 126f, float opacity = .60f) =>
        RegisterPointerLight(owner, tintProvider, glossWidth, opacity);
}
}
