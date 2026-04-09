using UnityEngine;
using UnityEngine.UI; // ต้องใช้สำหรับเรียก RawImage

public class RawImageToggler : MonoBehaviour
{
    [Header("UI Reference")]
    public RawImage displayRawImage; // ลาก RawImage ที่ต้องการเปลี่ยนใส่ช่องนี้

    [Header("Textures")]
    public Texture originalTexture;  // รูปต้นฉบับ
    public Texture newTexture;       // รูปใหม่ที่จะเปลี่ยน

    private bool isChanged = false;

    public void ToggleRawImage()
    {
        // สลับสถานะ
        isChanged = !isChanged;

        // เปลี่ยน Texture ตามสถานะ
        if (isChanged)
        {
            displayRawImage.texture = newTexture;
        }
        else
        {
            displayRawImage.texture = originalTexture;
        }
    }
}