import citybuilder as cb
from citybuilder import api


def main():
    values = cb.inputs()
    mode = values.get("mode", 0)
    if mode not in (0, 1, 2, 3, 4):
        raise ValueError("SCRIPT_HEIGHTMAP_INVALID_MODE")
    if mode >= 2:
        #Normalized float32 spacing above zero is 2^-24; the full range is 2 * maxHeight.
        step_meters = (0.001, 0.01, 0.1)[mode - 2]
        maximum = step_meters * (2 ** 23)
    else:
        maximum = values.get("maxHeight", 1000.0)
    api.terrain.rescale(maxHeight=maximum, rescaleHeights=mode == 1)


if __name__ == "__main__":
    main()
