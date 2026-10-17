using System.Collections.Generic;
using System.IO;

namespace CityDecoder {
    interface CityDecoder {
        void DecodeCity(ElementManager manager, string filename);
    }
}
