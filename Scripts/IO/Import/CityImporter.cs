using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SM = StringManager;

class CityImporter {
    public static void LoadCity(ElementManager manager, string name) {
        PreferencesManager.workingDirectory = name.Replace("/city.json.gz", "");
        if (File.Exists(name)) {
            var ext = PathHelper.GetExtension(name);
            switch (ext) {
                case "gz":
                    ReadCity(manager, new CityDecoder.JSON(), name);
                    break;
                default:
                    //error alert
                    break;
            }
        } else {
            manager.builder.CreateAlert(SM.Get("ERROR"), SM.Get("NO_CITY_FILE_ERROR"), SM.Get("OK"));
        }
    }

    static void ReadCity(ElementManager manager, CityDecoder.CityDecoder decoder, string filename) {
        MaterialManager.ClearCache(); //TODO: check if other stuff needs to be cleared
        decoder.DecodeCity(manager, filename);
    }

    //loads the city.json.gz of the folder, the result tells why it could not be loaded
    public static async Task<CityDecoder.LoadResult> LoadCityAsync(ElementManager manager, string folder, CityDecoder.LoadOptions options) {
        var name = folder.Replace("\\", "/").TrimEnd('/') + "/city.json.gz";
        if (!File.Exists(name)) return CityDecoder.LoadResult.Fail("not_found", SM.Get("NO_CITY_FILE_ERROR"));
        PreferencesManager.workingDirectory = name.Replace("/city.json.gz", "");
        MaterialManager.ClearCache();
        return await new CityDecoder.JSON().DecodeCityAsync(manager, name, options);
    }
}
