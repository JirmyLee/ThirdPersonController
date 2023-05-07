//===================================================
// FileName:      InputTest.cs         
// Author:        Allent Lee	
// CreateTime:    2023-04-26 22:09	
// E-mail:        xiaomo_lzm@163.com
// Description:   
//===================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputTest : MonoBehaviour
{
    private Animator animator;

    private float threshold = 0.1f; //键盘检测阈值
    // Start is called before the first frame update
    void Start()
    {
        animator = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // private void OnAnimatorMove()
    // {
    //     
    // }
    
    public void PlayerMove(InputAction.CallbackContext callbackContext)
    {
        Vector2 movement = callbackContext.ReadValue<Vector2>();
        if (movement.y > threshold)
        {
            animator.SetBool("move_forward",true);
        }
        else if (movement.y < threshold)
        {
            animator.SetBool("move_forward",false);
        }
        
        if (movement.x > threshold)
        {
            animator.SetBool("turn_right",true);
        }
        else if (movement.x < -threshold)
        {
            animator.SetBool("turn_left",true);
        }
        else
        {
            animator.SetBool("turn_right",false);
            animator.SetBool("turn_left",false);
        }
        Debug.Log(movement);
    }
}
