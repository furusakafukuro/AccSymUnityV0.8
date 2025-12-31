// IFieldProvider.cs
using UnityEngine;

public interface IFieldProvider
{
    // E [V/m], B [T], position [m], tPhys [s]
    Vector3 GetE(Vector3 position, double tPhys);
    Vector3 GetB(Vector3 position, double tPhys);

    // dt制御用の保守的な最大磁場 [T]（必須推奨）
    float GetBMaxEstimate();
}