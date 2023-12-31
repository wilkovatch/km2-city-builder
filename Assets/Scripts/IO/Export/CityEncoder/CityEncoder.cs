using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityEncoder {
    interface CityEncoder {
        void EncodeCity(ElementManager manager, string filename, System.Action post = null);
    }
}
