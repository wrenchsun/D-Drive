using System;
using UnityEngine;

namespace DDrive.Foundation.Values
{
    [Serializable]
    public struct ValueDef3
    {
        public ValueDef X;
        public ValueDef Y;
        public ValueDef Z;
        public bool Uniform;

        public Vector3 Evaluate(float t)
        {
            var x = X.Evaluate(t);
            return Uniform ? new Vector3(x, x, x) : new Vector3(x, Y.Evaluate(t), Z.Evaluate(t));
        }

        public Vector3 EvaluateAt(float elapsedSeconds)
        {
            var x = X.EvaluateAt(elapsedSeconds);
            return Uniform
                ? new Vector3(x, x, x)
                : new Vector3(x, Y.EvaluateAt(elapsedSeconds), Z.EvaluateAt(elapsedSeconds));
        }
    }
}
