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

## Details

- The texture you set on a patch is also used for the next patches.
- A patch with no texture is invisible. This is useful to make room for something else, such as buildings.
- A patch is optimized for flat ground. To make it follow a hill, choose **Internal** in **Point placement mode** and place points inside it. If it looks rough, raise **Smoothing**.
- Uncheck **Project points to ground** to keep the points where you put them.
