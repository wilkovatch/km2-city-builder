# Creating a city

## A new city

1. Select **File → New city**. If no city is loaded, this panel is already open on the right.
2. Set **Heightmap resolution**, **Terrain size** and **Maximum terrain height**. The sizes are in meters. A resolution and a size of 4096 suit an average city.
3. Choose the core in **Cores** and a starting point in **Templates**. Each template comes with its own presets.
4. Click **Select directory and create**, then create and select an **empty** folder for the city.

The empty city then opens.

## Saving and loading

| To do this | Use |
|---|---|
| Save | **File → Save city**, or Ctrl+S |
| Open another city | **File → Load city**, then select its folder |
| Discard the changes since the last save | **File → Reload city** |
| Close the city | **File → Close city** |

The last city you opened loads by itself at startup.

Everything about a city is in its folder. To make a backup, copy the folder. Files found through other search paths, such as the textures of the game, are not in it.

## Ground and terrain

The grid you see is only a guide for placing things, and it is not exported. The surface of your city is made later, with **terrain patches**.

The grid can still have a shape: use **Edit → Load heightmap** to import a heightmap, or **Edit → Toggle terrain edit menu** to raise and lower it by hand. Roads and terrain patches follow its shape by default.

To trace a real place, put a map image in the city folder and select it as the **Ground overlay** in the **Paths** tab of the preferences. Then turn it on with **View → Ground overlay**.
