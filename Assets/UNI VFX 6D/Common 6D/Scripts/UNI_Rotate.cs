using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UNI_VFX
{
    public class UNI_Rotate : MonoBehaviour
    {
        public float RotationSpeed = -135.0f;

        void Update()
        {
            transform.Rotate(0, RotationSpeed * Time.deltaTime, 0);
        }
    }
}