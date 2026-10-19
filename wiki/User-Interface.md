# User Interface

The user interface (`Scripts/UI`) is built entirely in code; the main scene only contains the root controls. The look and layout helpers are in `UILayout`.

## Main controller: `CityBuilderMenuBar` (`UI/MenuBar/CityBuilderMenuBar.cs`)

The script of the `Editor` control and the central coordinator of the editor.

* **Owns** every editor panel, the placers (`RoadPlacer`, `TerrainPlacer`, `BuildingPlacer`, `MeshPlacer`), the `TerrainClick` tool host, the file browser, the transform gizmo, the alert and text input popups.
* **Menus** (built in `ManualStart`): File (new, load, reload, save, export, close city, exit), Edit (project settings, city properties, terrain editing, heightmap, presets, object list, batch update, rotate city), View (ground overlay, overlay transparency, all traffic lanes, ground only, reset window), Create (road, terrain patch, building line, whole-city terrain, whole-city buildings, mesh instance), Settings and Help. An extra blue menu shows the array elements currently selected.
* **Selection**: `SelectObject(node, withEditor, collider, stack)` finds what was clicked (road, intersection, terrain patch, mesh instance, building line, building side, array object, city properties) and opens the matching panel with the matching placer; `DeselectObject`, `DeleteCurObj`, `EditCurSelectedObjectTransform`.
* **Change notifications**: `NotifyChange` (flags the world as changed, see [Architecture Overview](Architecture-Overview#the-update-loop)), `NotifyVisibilityChange`, `NotifyUpdateCompleted`.
* **Popups**: `CreateAlert` (message with one or two buttons), `CreateInput` (text input), `DoDelayed` (run an action on the next frame).
* **Lifecycle**: closing the window asks for confirmation while a city is open (`WantsToQuit`); the mesh pool is freed on exit. The Python server stops itself when the program's process ends, and is stopped explicitly when a city is erased.

`helper` is the `CityGroundHelper`; through it the menu bar reaches the `ElementManager`.

## Menu bar: `MenuBar`, `MenuBarElement` (`UI/MenuBar`)

`MenuBar` is the white strip at the top of the window. `AddElement(title, entries, actions, color)` adds a `MenuBarElement`: a button that opens a popup menu with the given entries.

## Editor panels

What the panels and the mouse tools do to the city is implemented in the operations layer (`Scripts/Operations`, see [Scripting API](Scripting-API)), shared with the scripting API: the panels and tools read the user input, call the operations and show their results (alerts, refreshed fields).

### Framework

| Class | Description |
|---|---|
| `EditorPanel` (`UI/EditorPanel/EditorPanel.cs`) | Abstract base of all panels. A panel has one or more pages (tabs), shown on the right side of the window, with an optional title field and tab buttons. `InitializeWithCustomParameters` builds a panel from a core type's `ui.json` and `parameters.json`: each tab element is either a *custom part* handled by the specific panel (`mainGroup`, ...), a `PRESET_<container>` preset dropdown, or a core parameter, turned into the right field by `AddParameters` (see [Parameters and UI](Parameters-and-UI)). It also implements array parameters (add, clone, move, delete, edit elements in an `ArrayElemEditorPanel`), custom elements and `objectInstance` buttons. Panels can be nested (`parentPanel`, `childPanels`, `Hide`, `GoUp`, `Terminate`). |
| `EditorPanelPage` (`UI/EditorPanel/EditorPanelPage.cs`) | One page: a scrollable column of rows. `AddLabel`, `AddButton`, `AddCheckbox`, `AddDropdown`, `AddInputField`, `AddSlider`, `AddScrollList`, `AddTextureField`, `AddMeshField`, `AddVectorInputField`... and the `AddField*` variants, which bind the element to a field of an object through a `FieldStatus`. `IncreaseRow` starts a new row; widths are factors of the default element width of 160 pixels (a panel is usually 1.5 units wide). |
| `EditorPanelElement` (`UI/EditorPanel/EditorPanelElement.cs`) | Base class of the elements: title, tooltip, tag, width, interactable state, `SetValue`, `actionEnabled`. |
| `FieldStatus` (`UI/EditorPanel/FieldStatus.cs`) | Binds an element to a value through a path such as `state.properties.totalWidth` or `State.properties.upperFloors.$floor.properties.height`: each part is a field, a dictionary key or (with `$name`) an array index provided by an index getter. Reads the value (`GetValue`), writes it with type conversion (`SetValue`, `SetInputFieldValue`) and notifies the change. |
| Elements (`UI/EditorPanel/Elements`) | `Button`, `Checkbox`, `Dropdown`, `Image`, `InputField`, `Label`, `MeshField` and `TextureField` (text field plus a button that opens the mesh/texture selector), `ScrollList` (virtualized list with optional thumbnails, driven by `ScrollListComponent`), `Slider`, `SliderWithInputField`, `TitleInputField`, `VectorInputField`. |
| Complex elements (`UI/EditorPanel/ComplexElements`) | `TypeSelector<T>` (dropdown of the core types of an element; changing it rebuilds the panel), `PresetSelector` (load/save/delete presets of a category, including editing presets on a dummy element), `IOList` (editable list with add/edit/delete and extra buttons, used for arrays, props and building floors). |

### Panels (`UI/EditorPanel/EditorPanels`)

| Panel | Purpose |
|---|---|
| `RoadEditorPanel` | Road editing: core parameters plus the built-in groups `mainGroup` (type, preset, point placement mode, projection, segments, smoothing, coplanarity, curve type), `intersectionGroup` (crosswalk sizes, intersection size increase and textures, roads through the intersection, connection heights) and `propsGroup` (prop rule preset and props containers). |
| `IntersectionEditorPanel` | Intersection type, preset, sidewalk segments and size increase (`mainGroup`), plus core parameters. |
| `TerrainPatchEditorPanel` | Terrain patch type, point placement mode (perimeter, internal, border mesh), smoothing, projection, auto-close, texture (`mainGroup`), and the border mesh list (`borderMeshGroup`). |
| `TerrainPatchBorderMeshEditorPanel` | The parameters of one border mesh. |
| `Buildings/LineEditorPanel` | Building line: type, preset, building list (`mainGroup`), built-in line properties (`propertiesGroup`) and automatic closing with presets (`loopGroup`). |
| `Buildings/BuildingEditorPanel` | One building: enabled sides, height, depth, roof texture, acute angle fix, and the side to edit. |
| `Buildings/SideEditorPanel` | One building side: ground floor and upper floor counts (`mainGroup`), and the block dictionary (`blocksGroup`). |
| `Buildings/FloorEditorPanel` | The slices of a floor. |
| `Buildings/BlockEditorPanel` | One block of the dictionary: block type and its parameters. |
| `Props/ContainerEditorPanel` | A props container of a road's prop rule: its type and its elements. |
| `Props/ElemEditorPanel` | A props element: type, preset, meshes and placement parameters. |
| `Props/PropMeshEditorPanel` | Mesh list of a props element. |
| `TransformPropertiesEditorPanel` | Position/rotation/scale of a mesh instance or parametric object, its mesh, and the mesh instance parameters of the core. On a control point, its position, and for road points the yaw and roll of the sections, set by hand or with the gizmo in local space restricted to the Y (yaw) and Z (roll) axes of the frame of the curve. |
| `ArrayElemEditorPanel` | One element of an `array` or `customElement` parameter. |
| `CityPropertiesEditorPanel` | The core's city properties. |
| `NewCityEditorPanel` | New city: terrain size, heightmap resolution, maximum height, core and template. Copies the template into the chosen folder and creates an empty `city.json.gz`. |
| `SettingsEditorPanel` | Global settings: core management (install, update, uninstall, open folder, add repository) and Python settings. |
| `ProjectSettingsEditorPanel` | City settings: graphics options, global and local extra asset folders, ground overlay. |
| `PresetEditorPanel` | Entry point for editing road, intersection and building presets. |
| `TerrainEditorPanel` | Heightmap tools (raise, lower, level) with range, intensity and continuous mode. |
| `HeightmapEditorPanel` | Loads a grayscale image as heightmap. |
| `WholeCityTerrainEditorPanel`, `WholeCityBuildingsEditorPanel` | Parameters of the city-wide generators. |
| `WholeCityRotationEditorPanel` | Rotates the whole city by 90° or 180°. |
| `BatchUpdateEditorPanel` | Changes parameters on every element of a kind that matches a condition. The `where` and `update` expressions use their own small syntax (`=`, `!=`, `and`, `or` over the element parameters), parsed by the nested `ExpressionTokenStream`/`ExpressionInputStream` classes. |
| `ObjectListEditorPanel` | Searchable list of the city's objects, with select and zoom. |
| `TextureListEditorPanel`, `MeshListEditorPanel` | Texture and mesh selectors (with thumbnails and regex search over the city folder and extra folders). |
| `CustomExporterEditorPanel` | Export dialog of a core exporter: one checkbox per flag. |
| `Helpers/RandomBuildingPresetsManager` | Shared list of building presets used for random generation. |

## Mouse tools

### `TerrainClick` (`UI/Actions/TerrainClick.cs`)

Runs every frame before the gizmo. When a tool (`modifier`, a `TerrainAction`) is active and the mouse is not over the UI, it raycasts from the mouse into the scene once per layer mask requested by the tool (`GetLayerMasks`), optionally snapping the hit to the grid (Shift), and passes the hits to the tool's `Apply`.

### `TerrainAction` and its subclasses

| Class | Description |
|---|---|
| `TerrainAction` (`UI/Actions/TerrainAction.cs`) | Base class: `ShowOnMap(hits)`, `Apply(hits)`, `GetLayerMasks()`. |
| `ElementPlacer.RoadPlacer` | Adds, inserts and moves road points, creates and joins intersections. Clicking the line along another road (`RoadSplitLine`) connects the road to its middle, splitting it (`RoadSplitter`). When this is the first point, the split waits for the second point. The placer only classifies what was clicked: the road being drawn and its connections are handled by `Operations.RoadDrawing`. |
| `ElementPlacer.TerrainPlacer` | Adds perimeter, internal and border mesh points to a terrain patch (snapping to anchors and existing points). |
| `ElementPlacer.BuildingPlacer` | Adds points to a building line and marks dividing points. |
| `ElementPlacer.MeshPlacer` | Places mesh instances, using the layer mask from the core's mesh instance rules. |
| `TerrainModifier.TerrainModifier` | Base of the heightmap tools: computes the affected heightmap area around the hit and calls `Scan`/`Deform`. |
| `TerrainModifier.RaiseFlat` | Raises or lowers the terrain. |
| `TerrainModifier.Level` | Levels the terrain to the clicked height or a fixed height. |
| `TerrainModifier.Null` | No-op tool used in selection mode: it hides the brush circle and selects the clicked object. |

`ActionHandlerManager` (`UI/Actions/ActionHandlerManager.cs`) creates the preview handle that follows the cursor; `Actions.Helpers` (`UI/Actions/Helpers.cs`) has helpers to set layers recursively, offset children and number new objects.

## World objects (`UI/World`)

| Class | Description |
|---|---|
| `CameraController` | The main camera, with two control schemes (`cameraControls` in the settings). Scheme A: fly camera with zoom (wheel, Page Up/Down), pan and zoom to point (middle button, Home), rotation (right button). Scheme B: orbit, pan (Shift) and zoom (Ctrl) with the middle button around a pivot picked under the center of the view, zoom toward the pivot with the wheel. Also rendering mode and per-layer cull distances. The middle button can be emulated with Alt + left button (`emulateMiddleButton`, see `GameEngine.Input.MiddlePressed`). |
| `Handle` | A draggable point (the `Handle.tscn` scene): color, scale and visibility, used for road points, terrain points and anchors. |
| `DiscHandle` | A disc-shaped handle. |

## Other UI classes

| Class | Description |
|---|---|
| `Alert`, `InputFieldPopup`, `ProgressBar` | Modal message box, text input box and loading bar. |
| `Tooltip` | Tooltips of the editor panel elements. |
| `FileBrowserHelper` | Open/save dialogs (native when available) and the list of the core's exporters (`GetCustomExporters`, read from `exporters/*/settings.json`). |
| `StringManager` | Translated strings, see [Settings, Paths and Python](Settings-Paths-and-Python#stringmanager). |
| `UIDetection`, `UIInputTracker` | Decide whether a mouse press was consumed by the UI, so that clicks on panels and dropdowns do not reach the world. `UIInputTracker` is an autoload. |
| `TextInputFocus` | Autoload with the input field focus rules (losing focus on any click elsewhere, end-of-edit events), so that editor shortcuts are not blocked by a focused field. |
| `TextFit` | Text wrapping and best-fit sizing on Godot labels and buttons. |
| `UILayout` | Helpers to place controls with rect values (anchors, pivot, size delta) and to create the default flat styles. |
| `RuntimeTheme.tres` | The Godot theme used by the whole UI. |

## Debug console (`UI/DebugConsole`)

A port of [yasirkula's In-game Debug Console](https://github.com/yasirkula/UnityIngameDebugConsole): `DebugLogManager` (autoload; collects the log messages and owns the window), `DebugLogLogger` (hooks into Godot's logging), `DebugLogEntry` (one message with its count), `DebugLogRecycledListView` and `DebugLogItem` (virtualized list), `DebugLogPopup` (the small button showing message counts when the console is closed), `DebugLogScrollbar`, `DebugConsoleUI`.
