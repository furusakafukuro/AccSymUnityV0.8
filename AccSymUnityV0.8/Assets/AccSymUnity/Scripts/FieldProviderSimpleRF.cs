// FieldProviderSimpleRF.cs (example implementation)
using System;
using UnityEngine;

public sealed class FieldProviderSimpleRF : MonoBehaviour, IFieldProvider
{
    [Header("Static Fields")]
    public Vector3 EStatic = Vector3.zero; // [V/m]
    public Vector3 BStatic = Vector3.zero; // [T]

    [Header("RF Traveling Wave (example)")]
    public bool enableRF = false;

    [Tooltip("RF frequency (Hz). ex: 2.856e9 or 3.52e8")]
    public double rfFreqHz = 2.856e9;

    [Tooltip("RF amplitude (V/m)")]
    public float rfE0 = 1.0e6f;

    [Tooltip("Propagation direction (unit vector)")]
    public Vector3 kDir = Vector3.forward;

    [Tooltip("Phase offset (rad)")]
    public double phi0 = 0.0;

    [Tooltip("Assume phase velocity ~ c (m/s).")]
    public double phaseVelocity = 299792458.0;

    [Tooltip("RF polarization direction (unit vector)")]
    public Vector3 eDir = Vector3.up;

    [Header("Bmax Estimate (Tesla) for dt control")]
    public float BMaxEstimateTesla = 1.0f;

    public Vector3 GetE(Vector3 position, double tPhys)
    {
        Vector3 E = EStatic;
        if (!enableRF) return E;

        Vector3 kN = (kDir.sqrMagnitude > 1e-12f) ? kDir.normalized : Vector3.forward;
        Vector3 eN = (eDir.sqrMagnitude > 1e-12f) ? eDir.normalized : Vector3.up;

        double omega = 2.0 * Math.PI * rfFreqHz;
        double k = omega / phaseVelocity;

        double z = Vector3.Dot(position, kN);
        double phi = omega * tPhys - k * z + phi0;

        float s = (float)Math.Sin(phi);
        E += eN * (rfE0 * s);
        return E;
    }

    public Vector3 GetB(Vector3 position, double tPhys)
    {
        return BStatic;
    }

    public float GetBMaxEstimate()
    {
        return BMaxEstimateTesla;
    }
}