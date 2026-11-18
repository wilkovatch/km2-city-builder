# Buildings

There are two ways to add buildings: **building lines**, which are generated from a few settings, and ready-made **meshes**. This chapter covers building lines. Meshes depend on the core and are in its own guides.

## The whole city at once

Add the roads and terrain first, then:

1. Select **Create → Whole city buildings**.
2. Choose the building line preset, and the building presets to use at random.
3. Set the minimum and maximum length of the buildings.
4. Click **Create whole city buildings**.

The same panel can delete all the buildings.

## One line by hand

1. Select **Create → Building line**, or press Ctrl+E.
2. Click to place the points of the line. You can place them on the anchors along roads and terrain patches, as for terrain patches.

A building line is a row of buildings, separated by **dividing points**. To add one, select **Dividing point** in **Point placement mode**. To change an existing point, set the mode to **None**, click the point and click **Toggle dividing point**.

To make a single building with its own shape, check **Front only** and **Loop** in the **Properties** tab, choose a roof texture and set the height.

## Filling a loop of roads

1. Place the first point of a line between two anchors. The line between them turns magenta when it is detected.
2. In the **Auto-Loop** tab, add the building presets to use, with **Add selected**. Each building uses one of them at random.
3. Set **Building min length** and **Building max length**, then click **Auto-close loop**.

## Reshaping a line

- To add a point in the middle of a line, set **Point placement mode** to **Point** (or **Dividing point**) and click on the line, between two of its points.
- To cut a line in two, click **Split**, then click a point that is not one of its ends, or anywhere on the line, where a point is added. Each building keeps its settings. Press Escape to cancel.
- To join two lines, click **Join with...** and then click the other line, which is outlined in green if it can be joined. They are joined at their closest ends. If those ends are apart, a new building fills the gap.

## Resizing a building

While you edit a building, the outline of its top is drawn in light blue and the outline of its back in pink (faintly where the building covers it). Click the top or the back, or near its outline: the gizmo appears in its middle. Drag it: the top sets the **Height**, the back the **Depth**. The fields in the panel follow.

The buildings of a line with **Front only** get their height from the line, so they can't be resized this way.

## Selecting parts of a building

With nothing selected:

- Click a side to select that side.
- Click a roof, or Ctrl-click any part, to select the building.
- Shift-click any part to select the whole line.

## How a building is described

Each building has a front, a back, a left and a right side, plus a roof. A side is made of **floors**, and each floor is made of **blocks**, which are its pieces of texture.

You rarely have to build this from nothing: start from a preset, and edit it only when you want something specific. The full manual explains blocks and floors in detail.
