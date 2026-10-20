namespace OpenTK.Input;

/// <summary>
/// Optional capability implemented by mouse drivers that can lock the pointer
/// (confine it and hide the cursor) for first-person mouse look.
/// </summary>
internal interface IMouseCursorController
{
	void SetPointerCapture(bool captured);
}
