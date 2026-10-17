-- pandoc filter for the quickstart guide PDF (see PostprocessBuild.BuildQuickstartGuide)
-- Sets the printed size of the screenshots (wiki/images): the full window ones (JPG) take the whole text width, the
-- cropped panels and menus (PNG) are all printed at the same scale, so that their text has the same size.

local pixelsPerCm = 55

local function findFile(src)
  for _, dir in ipairs(PANDOC_STATE.resource_path) do
    local file = io.open(dir .. "/" .. src, "rb")
    if file then return file end
  end
  return io.open(src, "rb")
end

-- the width in pixels from the IHDR chunk, nil if not a PNG
local function pngWidth(src)
  local file = findFile(src)
  if not file then return nil end
  local header = file:read(24)
  file:close()
  if not header or #header < 24 or header:sub(2, 4) ~= "PNG" then return nil end
  local b1, b2, b3, b4 = header:byte(17, 20)
  return ((b1 * 256 + b2) * 256 + b3) * 256 + b4
end

function Image(img)
  if img.attributes.width or img.attributes.height then return nil end
  local width = pngWidth(img.src)
  if width then
    img.attributes.width = string.format("%.2fcm", width / pixelsPerCm)
  else
    img.attributes.width = "100%"
  end
  return img
end
