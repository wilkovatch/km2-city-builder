# Buildings

There are two ways to add buildings: **building lines** and **polygonal buildings**, which are generated from a few settings, and ready-made **meshes**. This chapter covers the first two. Meshes depend on the core and are in its own guides.

## The whole city at once

Add the roads and terrain first, then:

1. Select **Create → Whole city buildings**.
2. Choose the building line preset, and the building presets to use at random.
3. Set the minimum and maximum length of the buildings.
4. Click **Create whole city buildings**.

The same panel can delete all the buildings.

## One line by hand

1. Select **Create → Buildings → Building line**, or press Ctrl+E.
2. Click to place the points of the line. You can place them on the anchors along roads and terrain patches, as for terrain patches.

A building line is a row of buildings, separated by **dividing points**. To add one, select **Dividing point** in **Point placement mode**. To change an existing point, set the mode to **None**, click the point and click **Toggle dividing point**.

To make a single building with its own shape, use a polygonal building (see below).

## A single building with its own shape

1. Select **Create → Buildings → Polygonal building**.
2. Click to place its corners, going around the building in either direction. You can place them on the anchors along roads and terrain patches, or on existing points.
3. In the **Properties** tab, set the height and choose a roof texture.

The outline goes through the points in the order you placed them, and it is always closed (from the last point back to the first). It can have inward corners, like an L-shaped building, and the facades always face outwards. To add a corner in the middle of a wall, click on its edge.

All the walls share one facade: click **Side properties** to edit it. To give each wall its own facade, check **Individual sides**, select the wall in the **Sides** list and click **Edit side**.

With nothing selected, click a wall to edit its facade, and click the roof or Shift-click a wall to edit the building.

Older cities made single buildings with the **Front only** option of the building lines. They become polygonal buildings when the city is migrated to the new version of its core.

## Filling a loop of roads

1. Place the first point of a line between two anchors. The line between them turns magenta when it is detected.
2. In the **Auto-Loop** tab, add the building presets to use, with **Add selected**. Each building uses one of them at random.
3. Set **Building min length** and **Building max length**, then click **Auto-close loop**.

## Reshaping a line

- To add a point in the middle of a line, set **Point placement mode** to **Point** (or **Dividing point**) and click on the line, between two of its points.
- To cut a line in two, click **Split**, then click a point that is not one of its ends, or anywhere on the line, where a point is added. Each building keeps its settings. Press Escape to cancel.
- To join two lines, click **Join with...** and then click the other line, which is outlined in green if it can be joined. They are joined at their closest ends. If those ends are apart, a new building fills the gap.

## Resizing a building

While you edit a building, the outline of its top is drawn in light blue and the outline of its back in pink (faintly where the building covers it). Click the top or the back, or near its outline: the gizmo appears in its middle. Drag it: the top sets the **Height**, the back the **Depth**. The gizmo has only the arrow of the direction that changes them: up and down for the top, towards and away from the front for the back. The fields in the panel follow.

A polygonal building has only its top: while you edit it, click its roof (or near its outline) and drag the gizmo up or down to set its **Height**.

## Selecting parts of a building

With nothing selected:

- Click a side to select that side.
- Click a roof, or Ctrl-click any part, to select the building.
- Shift-click any part to select the whole line.

## How a building is described

Each building has a front, a back, a left and a right side, plus a roof. A side is made of **floors**, and each floor is made of **blocks**, which are its pieces of texture.

You rarely have to build this from nothing: start from a preset, and edit it only when you want something specific. The full manual explains blocks and floors in detail.
