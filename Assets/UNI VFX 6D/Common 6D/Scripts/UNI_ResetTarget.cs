using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UNI_VFX
{
    public class UNI_ResetTarget : MonoBehaviour
    {
        public void ResetTarget()
        {
            transform.position = Vector3.zero;
        }
    }
}