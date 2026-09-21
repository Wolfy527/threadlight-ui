namespace Threadlight.EditorUI {
using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Collapses the Undo records produced by one continuous UI Toolkit interaction.
/// A gesture is only armed by user input and only becomes an Undo gesture after
/// the first matching value sample.
/// </summary>
public sealed class ThreadlightContinuousEditGesture : IDisposable {
    private enum InteractionKind {
        None,
        Pointer,
        Keyboard,
        Text
    }

    private static ThreadlightContinuousEditGesture active;

    private readonly VisualElement control;
    private readonly string undoName;
    private readonly bool textEditingControl;
    private readonly bool bound;
    private VisualElement panelRoot;
    private InteractionKind interaction;
    private int pointerId = -1;
    private KeyCode keyCode = KeyCode.None;
    private int floorGroup = -1;
    private bool changed;
    private bool disposed;

    static ThreadlightContinuousEditGesture() {
        AssemblyReloadEvents.beforeAssemblyReload += CompleteActive;
        EditorApplication.playModeStateChanged += _ => CompleteActive();
        EditorApplication.focusChanged += focused => {
            if (!focused)
                CompleteActive();
        };
    }

    private ThreadlightContinuousEditGesture(
        VisualElement control,
        string undoName,
        bool textEditingControl,
        bool bound) {
        this.control = control ?? throw new ArgumentNullException(nameof(control));
        this.undoName = string.IsNullOrWhiteSpace(undoName) ? "Edit Value" : undoName;
        this.textEditingControl = textEditingControl;
        this.bound = bound;

        control.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        control.RegisterCallback<PointerUpEvent>(OnPointerUp);
        control.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        control.RegisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        control.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        control.RegisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
        control.RegisterCallback<InputEvent>(OnInput, TrickleDown.TrickleDown);
        control.RegisterCallback<FocusOutEvent>(OnFocusOut);
        control.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        control.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        if (bound)
            control.RegisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);

        RegisterPanelRoot(control.panel?.visualTree);
    }

    /// <summary>
    /// Attaches to a Unity-bound control. Serialized changes are observed after
    /// binding has committed them to the target property.
    /// </summary>
    public static ThreadlightContinuousEditGesture AttachBound(
        VisualElement control,
        string undoName,
        bool textEditingControl = false) =>
        new ThreadlightContinuousEditGesture(
            control, undoName, textEditingControl, true);

    /// <summary>
    /// Attaches interaction lifecycle handling to a custom control. The caller
    /// must call MarkSample immediately before Undo.RecordObject.
    /// </summary>
    public static ThreadlightContinuousEditGesture AttachCustom(
        VisualElement control,
        string undoName,
        bool textEditingControl = false) =>
        new ThreadlightContinuousEditGesture(
            control, undoName, textEditingControl, false);

    /// <summary>
    /// Marks a real value sample. Calls made without a preceding user interaction
    /// are intentionally ignored.
    /// </summary>
    public void MarkSample() {
        if (disposed || active != this || interaction == InteractionKind.None)
            return;
        if (!changed) {
            floorGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
        }
        changed = true;
    }

    /// <summary>
    /// Confirms that the sample announced by <see cref="MarkSample"/> has been
    /// fully applied. Keeping the active range collapsed after every committed
    /// sample makes menu and programmatic Undo atomic without relying on a
    /// control-local key event.
    /// </summary>
    public void MarkSampleApplied() {
        if (disposed || active != this || !changed || floorGroup < 0)
            return;
        CollapseAppliedSamples();
    }

    public void Complete() {
        if (disposed)
            return;
        CompleteInternal();
    }

    public void Abandon() {
        if (disposed)
            return;
        AbandonInternal();
    }

    public static void CompleteActive() {
        active?.CompleteInternal();
    }

    /// <summary>
    /// Completes the active interaction only when it belongs to the supplied
    /// retained-mode tree.
    /// </summary>
    public static void CompleteActiveWithin(VisualElement ancestor) {
        if (active == null || ancestor == null ||
            !IsDescendantOf(active.control, ancestor))
            return;
        active.CompleteInternal();
    }

    public static void AbandonActive() {
        active?.AbandonInternal();
    }

    /// <summary>
    /// Abandons the active interaction only when it belongs to the supplied
    /// retained-mode tree. Undo listeners in one ThreadLight window must not
    /// terminate an edit owned by another window.
    /// </summary>
    public static void AbandonActiveWithin(VisualElement ancestor) {
        if (active == null || ancestor == null ||
            !IsDescendantOf(active.control, ancestor))
            return;
        active.AbandonInternal();
    }

    public void Dispose() {
        if (disposed)
            return;
        CompleteInternal();
        disposed = true;
        RegisterPanelRoot(null);
        control.UnregisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
        control.UnregisterCallback<PointerUpEvent>(OnPointerUp);
        control.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
        control.UnregisterCallback<PointerCaptureOutEvent>(OnPointerCaptureOut);
        control.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        control.UnregisterCallback<KeyUpEvent>(OnKeyUp, TrickleDown.TrickleDown);
        control.UnregisterCallback<InputEvent>(OnInput, TrickleDown.TrickleDown);
        control.UnregisterCallback<FocusOutEvent>(OnFocusOut);
        control.UnregisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        control.UnregisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        if (bound)
            control.UnregisterCallback<SerializedPropertyChangeEvent>(OnSerializedPropertyChanged);
    }

    private void Begin(
        InteractionKind next,
        int nextPointerId = -1,
        KeyCode nextKeyCode = KeyCode.None) {
        if (disposed)
            return;
        if (active != null && active != this)
            active.CompleteInternal();
        if (active == this && interaction != InteractionKind.None) {
            if (interaction == InteractionKind.Text && next == InteractionKind.Text)
                return;
            CompleteInternal();
        }
        active = this;
        interaction = next;
        pointerId = nextPointerId;
        keyCode = nextKeyCode;
        floorGroup = -1;
        changed = false;
    }

    private void CompleteInternal() {
        if (active != this)
            return;
        if (changed && floorGroup >= 0)
            CollapseAppliedSamples();
        ClearActive();
    }

    private void CollapseAppliedSamples() {
        Undo.FlushUndoRecordObjects();
        Undo.SetCurrentGroupName(undoName);
        Undo.CollapseUndoOperations(floorGroup);
    }

    private void AbandonInternal() {
        if (active == this)
            ClearActive();
    }

    private void ClearActive() {
        if (active == this)
            active = null;
        interaction = InteractionKind.None;
        pointerId = -1;
        keyCode = KeyCode.None;
        floorGroup = -1;
        changed = false;
    }

    private void OnPointerDown(PointerDownEvent evt) {
        bool text = textEditingControl || IsTextInputTarget(evt.target as VisualElement);
        Begin(text ? InteractionKind.Text : InteractionKind.Pointer, evt.pointerId);
    }

    private void OnPointerUp(PointerUpEvent evt) {
        if (active == this && interaction == InteractionKind.Pointer &&
            (pointerId < 0 || pointerId == evt.pointerId)) {
            CompleteInternal();
        }
    }

    private void OnPointerCancel(PointerCancelEvent evt) {
        if (active == this && interaction == InteractionKind.Pointer &&
            (pointerId < 0 || pointerId == evt.pointerId))
            CompleteInternal();
    }

    private void OnPointerCaptureOut(PointerCaptureOutEvent evt) {
        if (active == this && interaction == InteractionKind.Pointer)
            CompleteInternal();
    }

    private void OnKeyDown(KeyDownEvent evt) {
        if (IsUndoOrRedo(evt)) {
            CompleteActive();
            return;
        }
        if (IsModifierOrCommitKey(evt.keyCode))
            return;
        bool text = textEditingControl || IsTextInputTarget(evt.target as VisualElement);
        if (text) {
            Begin(InteractionKind.Text);
            return;
        }
        if (active == this && interaction == InteractionKind.Keyboard &&
            keyCode == evt.keyCode)
            return;
        Begin(InteractionKind.Keyboard, nextKeyCode: evt.keyCode);
    }

    private void OnKeyUp(KeyUpEvent evt) {
        if (active == this && interaction == InteractionKind.Keyboard &&
            keyCode == evt.keyCode) {
            CompleteInternal();
            return;
        }
        if (active == this && interaction == InteractionKind.Text &&
            (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter ||
             evt.keyCode == KeyCode.Escape))
            CompleteInternal();
    }

    private void OnInput(InputEvent evt) {
        if (active != this || interaction == InteractionKind.None)
            Begin(InteractionKind.Text);
    }

    private void OnFocusOut(FocusOutEvent evt) {
        if (active == this && (interaction == InteractionKind.Text ||
            interaction == InteractionKind.Keyboard)) {
            CompleteInternal();
        }
    }

    private void OnSerializedPropertyChanged(SerializedPropertyChangeEvent evt) {
        MarkSample();
        MarkSampleApplied();
    }

    private void OnAttachToPanel(AttachToPanelEvent evt) {
        RegisterPanelRoot(evt.destinationPanel?.visualTree);
    }

    private void OnDetachFromPanel(DetachFromPanelEvent evt) {
        CompleteInternal();
        RegisterPanelRoot(null);
    }

    private void RegisterPanelRoot(VisualElement nextRoot) {
        if (panelRoot == nextRoot)
            return;
        if (panelRoot != null)
            panelRoot.UnregisterCallback<PointerDownEvent>(
                OnPanelPointerDown, TrickleDown.TrickleDown);
        panelRoot = nextRoot;
        if (panelRoot != null)
            panelRoot.RegisterCallback<PointerDownEvent>(
                OnPanelPointerDown, TrickleDown.TrickleDown);
    }

    private void OnPanelPointerDown(PointerDownEvent evt) {
        if (active != this)
            return;
        VisualElement target = evt.target as VisualElement;
        if (target == null || !IsDescendantOf(target, control))
            CompleteInternal();
    }

    private static bool IsDescendantOf(VisualElement element, VisualElement ancestor) {
        for (VisualElement current = element; current != null; current = current.parent) {
            if (current == ancestor)
                return true;
        }
        return false;
    }

    private static bool IsTextInputTarget(VisualElement target) {
        for (VisualElement current = target; current != null; current = current.parent) {
            string typeName = current.GetType().Name;
            if (typeName.IndexOf("TextInput", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            foreach (string className in current.GetClasses()) {
                if (className.IndexOf("text-input", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        return false;
    }

    private static bool IsUndoOrRedo(KeyDownEvent evt) {
        EventModifiers actionModifiers = EventModifiers.Control | EventModifiers.Command;
        return (evt.modifiers & actionModifiers) != 0 &&
            (evt.keyCode == KeyCode.Z || evt.keyCode == KeyCode.Y);
    }

    private static bool IsModifierOrCommitKey(KeyCode keyCode) {
        switch (keyCode) {
            case KeyCode.None:
            case KeyCode.LeftShift:
            case KeyCode.RightShift:
            case KeyCode.LeftControl:
            case KeyCode.RightControl:
            case KeyCode.LeftAlt:
            case KeyCode.RightAlt:
            case KeyCode.LeftCommand:
            case KeyCode.RightCommand:
            case KeyCode.CapsLock:
            case KeyCode.Numlock:
            case KeyCode.ScrollLock:
            case KeyCode.Tab:
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
            case KeyCode.Escape:
                return true;
            default:
                return false;
        }
    }
}
}
