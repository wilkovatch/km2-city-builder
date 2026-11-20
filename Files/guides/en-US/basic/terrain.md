# Terrain

The grid is only a guide. The ground of your city is made of **terrain patches**, which fill the space between the roads.

## The whole city at once

Once the roads are finished, this is the quickest way:

1. Select **Create → Whole city terrain**.
2. Set the parameters for the patches. **Outer patch parameters** applies to the patch around the whole city.
3. Click **Create whole city terrain**.

The same panel has **Delete all terrain patches**, to start over.

## One patch by hand

1. Select **Create → Terrain patch**, or press Ctrl+T.
2. Click to place the points of its perimeter.

To connect a patch to the roads around it, click on their **anchor points**, shown in green. Hold Ctrl when you click an anchor linked to the last one, and all the anchors between them are added too.

To fill a loop of roads quickly, place the first point on an anchor of the loop and click **Auto-close loop**.

## Reshaping a patch

Click a patch to edit it. With **Point placement mode** set to **None**, click a point and move it with the gizmo.

- To add a corner, set the mode to **Perimeter** and click on a side of the patch: a new point is added there, between the two points of that side.
- To cut a patch, click **Split**, then draw the cut by clicking places one after the other: points of the perimeter, anywhere on a side (a point is added there), or anywhere inside the patch. The handle under the mouse is green where you can click, and red where the line would leave the patch or cross itself; the line drawn so far is shown in yellow.
  - A line from the perimeter to the perimeter again cuts the patch in two. With no places inside, the two places must not be next to each other.
  - A line back to its first place (inside the patch, or on the perimeter) carves the shape it encloses out of the patch, as a new patch. A patch can't have holes, so the ring left around the shape is also cut in two, along straight lines from the shape to the perimeter.
  - The patches stay joined along the cuts. Ctrl+Z removes the last place clicked, Escape cancels the cut.
- To join two patches that share a side, click **Merge with...** and then click the other patch. The patch under the mouse is outlined in green if it can be merged, and in red if not. The patch you were editing keeps its texture and settings.

## Details

To change the heightmap's vertical range, open **Scripts → Rescale heightmap**. **Range only** preserves elevations within the new float precision; **Rescale heights** scales hills and valleys around zero. Range-only shortcuts select **0.1 cm**, **1 cm** or **10 cm** positive height steps; negative heights have finer spacing. The custom maximum is ignored for shortcuts. A range that would clip terrain is refused, and the whole run is one undo step.

- The texture you set on a patch is also used for the next patches.
- A patch with no texture is invisible. This is useful to make room for something else, such as buildings.
- A patch is optimized for flat ground. To make it follow a hill, choose **Internal** in **Point placement mode** and place points inside it. If it looks rough, raise **Smoothing**.
- Uncheck **Project points to ground** to keep the points where you put them.
