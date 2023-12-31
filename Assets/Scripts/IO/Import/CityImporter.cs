using System.Collections.Generic;
using System.IO;
using UnityEngine;
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
}
