# Intersections

You don't place intersections directly: they appear when you connect roads.

## Connecting roads

While you place points, the places you can connect to are highlighted in cyan.

- **At the end of a road:** place the first or last point of your road on the end of another one. This creates an intersection there, or adds your road to the one that is already there.
- **In the middle of a road:** place the first or last point on the cyan line along the other road. That road is split in two, with the same properties, and an intersection is created between the parts.

If a split leaves a road with a strange shape, remove the control points close to the intersection or to the ends of the road.

To split a road without connecting anything to it, select **Split road** in its **Point placement mode**, then click on its cyan line.

## Editing an intersection

Click on an intersection to open its panel, where the core's settings for it are. You can also find it in **Edit → View object list**.

While you edit a road, its ends on intersections are shown as big points. Moving one moves the whole intersection, and the other connected roads follow.

## Sidewalks

The **Sidewalks** tab of the intersection panel sets how smooth the sidewalk corners are:

- **Inner segments** is the number of segments of the curve along the road, **Outer segments** the one of the other side of the sidewalk (0 for the same). With fewer outer segments the outer corner is simpler, for example a sharp corner with 2.
- **Outer side skips the crosswalk segments** makes the outer side curve from road end to road end, instead of running straight along the crosswalks first. With 1 outer segment the corner is cut straight.

Below them is the list of the sidewalks of the intersection. Select one, or click on it in the view, to see it outlined in green. Tick **Override** to give it its own segments, which are kept only for that intersection, not in the presets.

## Joining two roads again

An intersection with exactly two roads has two extra options:

- **Merge into** followed by a road name turns the two roads into one, keeping the properties of the road you name.
- **Seamless** keeps two separate roads but joins them smoothly, with no intersection surface between them. It is useful to change the properties of a road partway along it.

A seamless intersection has no surface to click. To reach it, open the **Inters.** tab of one of its roads and click the button to edit its start or end intersection.
