# Exporting and next steps

## Check, then export

1. Save the city with Ctrl+S.
2. Select **File → Validate city**. This runs the exporter without keeping its files, and lists the errors and warnings it finds. Select a line and click **Zoom to element** or **Edit element** to see the problem.
3. Select **File → Export city**, choose where to save it and click **Save**. Most cores then show a dialog with their export options.

The result depends on the core: see its guides for what is written and where it goes.

## When something goes wrong

The counters at the bottom left of the window show the number of messages, warnings and errors. Click them to open the debug console, which shows each error.

Python errors are saved in the file `pythonLog.txt`, in the folder of the program.

## Where to go from here

- **Help → Guides** has the guides of each installed core, including everything specific to the game.
- **Help → Quickstart guide** opens the full manual, which covers everything in this guide in more depth, as well as props, batch updates and other tools.
- **Help → Controls** lists the current controls.
- The [project wiki](https://github.com/wilkovatch/km2-city-builder/wiki) has the rest of the documentation, including scripting and how to make a core.
