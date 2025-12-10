using Metaplay.Unity;
using Metaplay.Unity.DefaultIntegration;
using UnityEngine;
using UnityEngine.UI;

public class ConnectionHealthIndicator : MonoBehaviour
{
    Image _image;

    void Start()
    {
        _image = GetComponent<Image>();
    }

    void Update()
    {
        // Show indicator if connection is in unhealthy state
        bool isUnhealthy = MetaplayClient.ConnectionHealth == ConnectionHealth.Unhealthy;
        _image.enabled = isUnhealthy;

        // Scale indicator to make it more visually apparent
        if (isUnhealthy)
        {
            float scale = 1.0f + 0.1f*Mathf.Sin(Time.time * 3.0f * Mathf.PI);
            transform.localScale = new Vector3(scale, scale, scale);
        }
    }
}
