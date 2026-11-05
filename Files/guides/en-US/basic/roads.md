# Roads

Roads are the backbone of a city, so they come first.

## Drawing a road

1. Select **Create → Road**, or press Ctrl+R.
2. Click on the ground to place its points. The road appears as a curve through them.
3. Press Escape or click **End editing** when you are done.

To continue an existing road, select it and choose **Add after last** in **Point placement mode**. **Add before first** extends it from its start, and **Insert after selected** adds a point in the middle.

## Its shape

The **Spline type** setting decides how the points make a curve:

- **Bézier** is the default.
- **Hermite** passes through its points.
- **Low poly** behaves like Hermite, with one segment for each point. Use it when you want full control over the geometry.

To edit the shape, set the mode to **None** and drag the points.

Roads follow the ground by default. Uncheck **Project CPs to ground** to keep the points where you put them, for example on a bridge.

## Its properties

The panel of the road has several tabs, which depend on the core. Widths, textures, lanes and props are all set there.

## Presets

Setting up every road by hand would be slow, so each type of object has **presets**:

- Pick a preset in the dropdown near the top of the panel to apply it. New roads use the last one you picked.
- **Save preset** stores the current values as a preset.
- **Reset** goes back to the values of the preset.
- **Sync** applies a preset to every object linked to it. Use it after changing the preset.
- **Unlink** keeps the values but removes the link to the preset.

A template usually comes with presets for common roads, so start by looking through those.
