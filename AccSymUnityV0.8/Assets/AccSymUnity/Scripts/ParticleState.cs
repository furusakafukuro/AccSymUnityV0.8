// ParticleState.cs
using System;
using UnityEngine;

[Serializable]
public struct ParticleState
{
    public Vector3 position; // [m]  (Unity unit = meter 前提)
    public Vector3 u;        // [m/s] u = gamma * v  (相対論運動量変数)

    public float charge;     // [C]
    public float mass;       // [kg]
}