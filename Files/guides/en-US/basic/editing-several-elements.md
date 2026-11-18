# Editing several elements

## Moving a part of the city

To move, rotate or resize several elements together, such as a block with its roads, terrain and buildings:

1. Select **Edit → Select multiple elements**.
2. Click each element to add it to the selection. Click it again to remove it. You can also drag a rectangle around the elements. Hold Ctrl while dragging to remove them instead.
3. A yellow handle appears at the center of the selection. Drag it with the gizmo, and everything selected follows.
4. Click **Move**, **Rotate** or **Scale** in the panel to change what the gizmo does.

The selected elements have a yellow outline. **Select all** selects the whole city, and **Delete selected** deletes the selection, after asking.

Some points do not move on their own, because they follow something else:

- A point of a terrain patch or building line placed on an anchor follows its road. Select the road too, and it moves with it.
- The end of a road on an intersection stays there. Select the intersection too, and all its roads follow it.

Each drag is a single step for **Edit → Undo**.

## Copying properties

To give an element the same settings as another one, without saving a preset:

1. Click the element to change, so that its editor is open.
2. Select **Edit → Copy properties from...**.
3. Click the element to copy from. The element under the mouse is outlined in green if you can copy from it, and in red if not.

This works between two elements of the same kind: roads, intersections, terrain patches, building lines, buildings, and the elements and meshes of a core when they are of the same type. To copy a building, open its editor first, then click another building.

Only the settings are copied. The points of the element, and where it is, stay as they are.

With **Select multiple elements** open, the same menu entry (or **Copy properties from...** in the panel) gives the settings of the element you click to every selected element of its kind.

Press Escape to cancel.
