# Installing a core

You need at least one core to create a city.

## Install it

1. If a city is open, close it with **File → Close city**. Cores can only be managed when no city is loaded.
2. Open **Settings → Preferences** and go to the **Cores** tab.
3. Select a core in the **Available cores** list.
4. Click **Install**, and wait for the message that confirms it.

The list already includes the known cores. For any other core, click **Add from repository** and enter the address of its Git repository.

From the same tab you can also **Check updates**, **Uninstall** a core, or **Open folder** to see its files.

## Python

Cores use Python to read game files and export cities. The program can manage its own Python installation, and then it installs what each core needs by itself.

To use your own installation instead, set its path in the **Python** tab of the preferences. In that case you have to install the packages the core requires yourself.

## The files of the game

Many cores work with the textures and meshes of the game, but cores can't include them: you provide them from your own copy of the game. The guides of each core explain how.

The editor looks for textures and meshes in the folder of the city, and in the folders listed in the **Paths** tab of the preferences. If you add files while the editor is open, click **Rescan folders** in the mesh list to see them.
