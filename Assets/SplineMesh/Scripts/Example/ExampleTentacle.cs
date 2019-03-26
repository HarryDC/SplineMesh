﻿using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SplineMesh {
    /// <summary>
    /// Example of component to bend a mesh along a spline with some interpolation of scales and rolls. This component can be used as-is but will most likely be a base for your own component.
    /// 
    /// For explanations of the base component, <see cref="ExamplePipe"/>
    /// 
    /// In this component, we have added properties to make scale and roll vary between spline start and end.
    /// Intermediate scale and roll values are calculated at each spline node accordingly to the distance, then given to the MeshBenders component.
    /// MeshBender applies scales and rolls values by interpollation if they differ from strat to end of the curve.
    /// 
    /// You can easily imagine a list of scales to apply to each node independantly to create your own variation.
    /// </summary>
    [ExecuteInEditMode]
    [SelectionBase]
    [DisallowMultipleComponent]
    public class ExampleTentacle : MonoBehaviour {
        private GameObject generated;
        private Spline spline = null;
        private bool toUpdate = false;

        public Mesh mesh;
        public Material material;
        public Vector3 rotation;
        public float startScale = 1, endScale = 1;
        public float startRoll = 0, endRoll = 0;

        private void OnEnable() {
            string generatedName = "generated by " + GetType().Name;
            var generatedTranform = transform.Find(generatedName);
            generated = generatedTranform != null ? generatedTranform.gameObject : UOUtility.Create(generatedName, gameObject);

            spline = GetComponentInParent<Spline>();
            spline.NodeListChanged += (s, e) => { toUpdate = true; };
        }

        private void OnValidate() {
            if (spline == null)
                return;

            // apply scale and roll at each node
            float currentLength = 0;
            foreach (CubicBezierCurve curve in spline.GetCurves()) {
                float startRate = currentLength / spline.Length;
                currentLength += curve.Length;
                float endRate = currentLength / spline.Length;

                curve.n1.Scale = Vector2.one * (startScale + (endScale - startScale) * startRate);
                curve.n2.Scale = Vector2.one * (startScale + (endScale - startScale) * endRate);

                curve.n1.Roll = startRoll + (endRoll - startRoll) * startRate;
                curve.n2.Roll = startRoll + (endRoll - startRoll) * endRate;
            }
            toUpdate = true;
        }

        private void Update() {
            if (toUpdate) {
                toUpdate = false;
                CreateMeshes();
            }
        }

        public void CreateMeshes() {
            UOUtility.DestroyChildren(generated);

            int i = 0;
            foreach (CubicBezierCurve curve in spline.GetCurves()) {
                GameObject go = UOUtility.Create("SplineMesh" + i++,
                    generated,
                    typeof(MeshFilter),
                    typeof(MeshRenderer),
                    typeof(MeshBender));
                go.GetComponent<MeshRenderer>().material = material;
                MeshBender mb = go.GetComponent<MeshBender>();
                mb.Source = SourceMesh.Build(mesh)
                    .Rotate(Quaternion.Euler(rotation));
                mb.SetInterval(curve);
                mb.ComputeIfNeeded();
            }
        }
    }
}
