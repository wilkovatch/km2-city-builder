using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IGroundable {
    public bool IsProjectedToGround();
    public bool RemovePoint(object point);
}