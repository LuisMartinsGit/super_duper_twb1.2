using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UNI_VFX
{
    public class UNI_MouseTargeting : MonoBehaviour
    {
        public GameObject target;
        public Transform origin;
        public float fallbackDistance = 20f;
        public LayerMask hitLayers;

        void Update()
        {
            if (target == null || origin == null) return;

            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, Mathf.Infinity, hitLayers))
            {
                target.transform.position = hit.point;
            }
            else
            {
                Vector3 mouseDirection = ray.direction;
                target.transform.position = origin.position + (mouseDirection * fallbackDistance);
            }
        }
    }
}