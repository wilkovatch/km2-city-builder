using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IAnchorable {
    public bool IsDeleted();
    public bool IsMoveable();
    public Vector3 GetPosition();
}