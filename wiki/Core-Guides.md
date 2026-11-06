# Core Guides

A core can include guides for its users, written in Markdown. They appear in *Help→Guides*, in a submenu with the name of the core, and open in the guide panel of the program, on the left side of the window.

Guides are optional. They are listed with no city loaded, for every installed core, so a guide can explain how to create the first city with that core.

The program uses the same mechanism for its own *Basic guide*, in `Files/guides`.

## Files

```
<CoreName>/guides/<language>/<guide id>/
├── guide.json       the title, the position in the menu and the list of chapters
├── <chapter>.md     one file for each chapter
└── images/          optional
```

`<language>` is the language of the program (`en-US`). If there is no folder for the current language, the `en-US` one is used.

```json
{
    "title": "Beginner guide",
    "order": 1,
    "chapters": ["before-you-start", "roads", "exporting"]
}
```

| Key | Description |
|---|---|
| `title` | The name in the menu and in the title bar of the panel. |
| `order` | The position among the guides of the core (lowest first). With the same `order`, guides are sorted by title. |
| `chapters` | The chapters in order: the names of their `.md` files, without the extension. |

Each chapter starts with a level 1 heading (`# Title`), which is also its name in the list of chapters.

A guide with a missing or invalid `guide.json`, or without chapters, is skipped with a warning. A chapter whose file can't be read shows a message in the panel.

## Markdown

The supported subset:

| Element | Syntax |
|---|---|
| Headings | `#`, `##`, `###` |
| Emphasis | `**bold**`, `*italic*`, `***both***` |
| Inline code | `` `code` `` |
| Lists | `- item`, `1. item`, nested with indentation |
| Tables | `\| A \| B \|`, with a `\|---\|---\|` line after the header |
| Code blocks | Lines between two ```` ``` ```` lines |
| Notes | `> text` |
| Horizontal rule | `---` |
| Links | `[text](target)` |
| Images | `![description](images/file.png)` |

Only `*` marks emphasis, as `_` is common in the names of game files. Escape a special character with `\`. Two spaces at the end of a line break the line.

A paragraph inside a list item isn't supported: an item is its text and an optional nested list.

## Links

- A relative link to a `.md` file opens that chapter in the panel: `roads.md`, or `../advanced/roads.md` for a chapter of another guide.
- An `http` or `https` link opens in the browser.
- Any other link does nothing and logs a warning. Guides come from the repository of the core, so the panel never opens local files or other schemes.

## Images

The path of an image is relative to the folder of the guide, and has to stay inside it. Images wider than 300 pixels fit the width of the text, the others keep their size. If an image can't be shown, its description is shown instead.

Remember that the original files of a game must not be committed: this includes screenshots that show them, if you treat these as game data.

## Checking the guides

`selftest undo <new folder> --mode guide` checks all the guides of the installed cores: every chapter has a title and a body, every internal link and image exists, and no Markdown is left unparsed. See the [agent testing notes](https://github.com/wilkovatch/km2-city-builder/blob/main/docs/agents/testing.md#self-tests-the-test-suite) of the program.

## Internals

| Class | Purpose |
|---|---|
| `GuideLibrary` (`UI/Guide/GuideLibrary.cs`) | Finds the guides, and resolves their links and images. It reads the folders of all the cores directly, so it needs no city and doesn't touch the settings of the current core. |
| `GuideMarkdown` (`UI/Guide/GuideMarkdown.cs`) | The parser, which is independent of the UI, and the renderer, which uses the tag stack of a `RichTextLabel` rather than BBCode. |
| `GuidePanel` (`UI/Guide/GuidePanel.cs`) | The panel. See [User Interface](User-Interface). |
