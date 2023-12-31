km2 City Builder is a city editor to create custom cities for use in videogames. (made with Unity, currently version 2022.3.15f1)

Support for each game is added via a core that defines (in json files and Python code) how the geometry has to be generated, how to read textures and meshes, and how and export the game files.

Cores can be installed either manually or from Git repositories, a list of available repositories is included in the file coreRepos.json.

Python 3 is required but only at runtime, its path has to be specified in the file settings.json, but if missing its installation can be handled by the city builder itself, via a script in the pythonInstaller folder.

To setup the project, you need to open it in Unity to make it install the required packages (ignore warnings about compilation errors), then, once it's open, close it and run the file run_setup.bat. (this will take care of the missing parts that cannot be installed automatically)

Note that to complete the setup you need Java 11 or later installed. (this is required for ANTLR 4, which is used to generate parsers for the expressions used in the cores)

At this point if you reopen it there should not be any compilation errors. Make sure to open the scene "Scenes/CityBuilder" before entering play mode.

TODO: Documentation for creating cores
