using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityDecoder {
    interface CityDecoder {
        void DecodeCity(ElementManager manager, string filename);
    }
}
