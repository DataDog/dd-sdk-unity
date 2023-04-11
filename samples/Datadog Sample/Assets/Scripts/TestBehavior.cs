using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Datadog.Unity;

public class TestBehavior : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        var datadog = new Datadog.Unity.Datadog();
        datadog.Init();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
