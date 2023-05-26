//===================================================
// FileName:      ThridPersonController.cs         
// Author:        Allent Lee	
// CreateTime:    2023-05-07 00:48:21	
// E-mail:        xiaomo_lzm@163.com
// Description:   
//===================================================

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.InputSystem;

//玩家姿态
public enum PlayerPosture
{
    Crouch,     //下蹲
    Stand,      //站立
    Jumping,    //跳跃
    Falling,    //高处跌落
    Landing,    //着陆（玩家刚落地还不能起跳的状态）
    Climbing    //攀爬
}

//运动状态
public enum LocomotionState
{
    Idle,
    Walk,
    Run
}

//战斗状态
public enum AttackState
{
    Normal,
    Attack
}

public class ThirdPersonCtrl : MonoBehaviour
{
    private Transform playerTransform;
    private Transform cameraTransform;
    private Animator animator;
    private CapsuleCollider capsuleCollider;
    
    // private AnimatorStateInfo animState;    //动画状态
    public Transform rightHandObject;           //刀柄IK位置

    [HideInInspector]
    public PlayerPosture playerPosture = PlayerPosture.Stand;
    private const float crouchThreshold = 0f;   //和状态机里空手运动BlendTree的阈值一致
    private const float standThreshold = 1f;
    private const float midairThreshold = 2.1f; //多0.1保证跳跃时参数大于2，防止抖动
    private float landingThreshold = 1f;  //着陆强度阈值（可以用下蹲强度表示着陆强度）

    [HideInInspector]
    public LocomotionState locomotionState = LocomotionState.Idle;
    [HideInInspector]
    public AttackState attackState = AttackState.Attack;
    private const float crouchSpeed = 1.5f;   //蹲着走速度
    private const float walkSpeed = 2.5f;
    private const float runSpeed = 5.5f;

    private Vector2 MoveInput;
    private bool isRuPress;
    private bool isCrouchPress;
    private bool isJumpPress;
    private bool isAttackPress;

    private int postureHash;    //动画状态机玩家姿态参数哈希值，使用哈希值比直接使用字符串效率高
    private int moveSpeedHash;
    private int turnSpeedHash;
    private int verticalVelHash;
    private int gridSwordIKHash;

    Vector3 playerMovement = Vector3.zero;  //玩家实际要移动的方向
    private bool isGrounded = false;//玩家是否着地
    private float groundCheckOffset = 0.2f; //地面检测射线偏移量
    public float gravity = -15f;    //模拟重力，很多游戏重力都超过实际
    private float verticalVelocity; //当前角色垂直方向速度
    public float maxHeight = 1.2f;   //最大跳跃高度
    private float fallMultiplier = 1.5f;    //下落速度相对跳跃速度的倍率，下落速度快虽然反物理，但能增强游戏质感

    private float jumpCD = 0.5f;    //跳跃CD
    private bool isLanding;
    
    private bool couldFall;         //玩家是否可以跌落
    private float fallHeight = 0.5f;        //跌落的最小高度，小于此高度不会切换到跌落姿态
    
    private static readonly int CACHE_SIZE = 3; //离地前几帧速度缓存帧数
    private Vector3[] velCache = new Vector3[CACHE_SIZE];
    private int currentCacheIndex = 0;  //缓存词中最老的向量索引
    Vector3 averageVel = Vector3.zero;

    private PlayerSensor playerSensor;  //障碍物检测脚本
    Vector3 PlayerMomentWorldSpace = Vector3.zero;  //玩家相对世界坐标上的输入
    private bool isClimbReady;          //是否能攀爬
    private int defaultClimbParameter = 0;
    private const int vaultParamter = 1;        //翻越阈值
    private const int lowClimbParamter = 2;     //低位攀爬阈值
    private const int highClimbParamter = 3;    //高位攀爬阈值
    private int currentClimbParamter;
    private Vector3 rightHandPosition;         //攀爬时右手位置
    
    // Start is called before the first frame update
    void Start()
    {
        playerTransform = this.transform;   //提升访问效率，避免每次都去访问组件
        cameraTransform = Camera.main.transform;
        animator = GetComponent<Animator>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        playerSensor = GetComponent<PlayerSensor>();
        
        // animState = animator.GetCurrentAnimatorStateInfo(animator.GetLayerIndex("Base Layer"));
        postureHash = Animator.StringToHash("玩家姿态");
        moveSpeedHash = Animator.StringToHash("移动速度");
        turnSpeedHash = Animator.StringToHash("转弯速度");
        verticalVelHash = Animator.StringToHash("垂直速度");
        gridSwordIKHash = Animator.StringToHash("GridSwordIK");
    }

    // Update is called once per frame
    void Update()
    {
        CheckGrounded();
        SwitchPlayerStates();
        CaculateGravity();
        Jump();
        CaculateInputDirection();
        SetupAnimator();
        //PlayFootStep();
        //animator.SetLayerWeight(1,1-animator.GetFloat(gridSwordIKHash));
    }

    #region 输入相关
    public void GetMoveInput(InputAction.CallbackContext ctx)
    {
        MoveInput = ctx.ReadValue<Vector2>();
    }
    public void GetRunInput(InputAction.CallbackContext ctx)
    {
        isRuPress = ctx.ReadValueAsButton();
    }
    public void GetCrouchInput(InputAction.CallbackContext ctx)
    {
        isCrouchPress = ctx.ReadValueAsButton();
    }
    public void GetAttackInput(InputAction.CallbackContext ctx)
    {
        isAttackPress = ctx.ReadValueAsButton();
    }

    public void GetJumpInput(InputAction.CallbackContext ctx)
    {
        isJumpPress = ctx.ReadValueAsButton();
        //Debug.Log("is jump!");
    }
    #endregion
    
    private void OnAnimatorIK(int layerIndex)
    {
        if (animator.GetFloat(gridSwordIKHash) < 0.5f)
        {
            animator.SetIKPosition(AvatarIKGoal.RightHand, rightHandObject.position);
            animator.SetIKRotation(AvatarIKGoal.RightHand, rightHandObject.rotation);
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 1f );
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 1f );
        }
    }
    
    //切换玩家状态
    void SwitchPlayerStates()
    {
        switch (playerPosture)
        {
            case PlayerPosture.Stand:
                if (verticalVelocity > 0)   //垂直速度>0说明处于跳跃姿态
                {
                    playerPosture = PlayerPosture.Jumping;
                }
                else if (!isGrounded && couldFall)
                {
                    playerPosture = PlayerPosture.Falling;
                }
                else if (isCrouchPress)
                {
                    playerPosture = PlayerPosture.Crouch;
                }
                else if (isClimbReady)
                {
                    playerPosture = PlayerPosture.Climbing;
                }
                isClimbReady = false;
                break;
            case PlayerPosture.Crouch:
                if (!isGrounded && couldFall)
                {
                    playerPosture = PlayerPosture.Falling;
                }
                else if (!isCrouchPress)
                {
                    playerPosture = PlayerPosture.Stand;
                }
                isClimbReady = false;
                break;
            case PlayerPosture.Falling:
                if (isGrounded)
                {
                    StartCoroutine(CoolDownJump());
                }

                if (isLanding)
                {
                    playerPosture = PlayerPosture.Landing;
                }
                isClimbReady = false;
                break;
            case PlayerPosture.Jumping:
                if (isGrounded)
                {
                    StartCoroutine(CoolDownJump());
                }
                // else if (isClimbReady)
                // {
                //     playerPosture = PlayerPosture.Climbing;
                // }
                
                if (isLanding)
                {
                    playerPosture = PlayerPosture.Landing;
                }
                isClimbReady = false;
                break;
            case PlayerPosture.Landing:
                if (!isLanding)
                {
                    playerPosture = PlayerPosture.Stand;
                }
                isClimbReady = false;
                break;
            case PlayerPosture.Climbing:
                AnimatorController animatorController = animator.runtimeAnimatorController as AnimatorController;
                AnimatorStateMachine rootStateMachine = animatorController.layers[0].stateMachine; // 假设使用的是第一个Layer
                AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0); // 获取第一个Layer的当前状态
                if (!currentState.IsName("ClimbHigh") && !currentState.IsName("ClimbLow")  && !animator.IsInTransition(0))
                {
                    playerPosture = PlayerPosture.Stand;
                }
                break;
        }
        

        if (MoveInput.magnitude == 0)   //玩家没输入
        {
            locomotionState = LocomotionState.Idle;
        }
        else if (!isRuPress)
        {
            locomotionState = LocomotionState.Walk;
        }
        else
        {
            locomotionState = LocomotionState.Run;
        }

        if (isAttackPress)
        {
            attackState = AttackState.Attack;
        }
        else
        {
            attackState = AttackState.Normal;
        }
    }
    
    //计算移动方向
    void CaculateInputDirection()
    {
        // Vector3 camForwawrdProjection = new Vector3(MoveInput.x, 0, MoveInput.y);
        // float y = cameraTransform.rotation.eulerAngles.y;
        // playerMovement = Quaternion.Euler(0, y, 0) * camForwawrdProjection;
        Vector3 camForwawrdProjection = new Vector3(cameraTransform.forward.x, 0, cameraTransform.forward.z).normalized;//相机在水平方向的投影
        PlayerMomentWorldSpace = MoveInput.y * camForwawrdProjection + MoveInput.x * cameraTransform.right;

        playerMovement = playerTransform.InverseTransformVector(PlayerMomentWorldSpace);   //将世界坐标转为本地坐标
    }
    
    //检测是否在地上
    private void CheckGrounded()
    {
        Vector3 pointBottom, pointTop;  //分别为胶囊体起点球心，胶囊体终点球心
        //- playerTransform.up * 0.02f是为了在脚步往下一点的地方进行检测， - playerTransform.forward * 0.04是在角色中心往后一点检测，防止对着墙跳时能卡在中间
        pointBottom = playerTransform.position + playerTransform.up * capsuleCollider.radius - playerTransform.up * 0.02f - playerTransform.forward * 0.04f;
        pointTop = playerTransform.position + playerTransform.up * capsuleCollider.height - playerTransform.up * capsuleCollider.radius -playerTransform.forward * 0.04f;
        LayerMask ignoreMask = ~(1 << LayerMask.NameToLayer("Player")); //LayerMask.GetMask("Player")
        Debug.DrawLine(pointBottom, pointTop,Color.green);
 
        // RaycastHit hitInfo;
        // if (Physics.SphereCast(playerTransform.position, capsuleCollider.radius, Vector3.down, out hitInfo,
        //         ((capsuleCollider.height / 2f) - capsuleCollider.radius) + advancedSettings.groundCheckDistance))
        
        // Collider[] colliders = Physics.OverlapCapsule(pointBottom, pointTop, capsuleCollider.radius, ignoreMask);
        // if (colliders.Length != 0)
        if(Physics.CheckCapsule(pointBottom, pointTop, capsuleCollider.radius, ignoreMask))
        {
            Debug.LogFormat("is ground");
            isGrounded = true;
        }
        else
        {
            Debug.Log("not int ground");
            isGrounded = false;
            
            //角色没有着地就检测角色脚0.5米内是否能够检测到地面，如果能则认为可以跌落
            couldFall = !Physics.Raycast(playerTransform.position, Vector3.down, fallHeight, ignoreMask);
        }

        return;
    }

    //计算重力影响
    void CaculateGravity()
    {
        if (playerPosture != PlayerPosture.Jumping && playerPosture != PlayerPosture.Falling)
        {
            if (!isGrounded)
            {
                verticalVelocity += gravity * fallMultiplier * Time.deltaTime;   //不是跳跃也不是跌落状态，但双脚离地，此时也要累加重力加速度，让下楼梯这种短距离地能下落
            }
            else
            {
                verticalVelocity = 0;   //如果检测到地面，垂直速度归零。如果不归零（如character controller使用gravity * Time.deltaTime），在斜坡会缓慢往下掉
            }
            return;
        }
        else
        {
            if (verticalVelocity <= 0 || !isJumpPress)  //下落阶段
            {
                verticalVelocity += gravity * fallMultiplier * Time.deltaTime;
            }
            else
            {
                verticalVelocity += gravity * Time.deltaTime;   //重力加速度
            }
        }
    }

    //等待跳跃CD
    IEnumerator CoolDownJump()
    {
        //计算landingThreshold，让它处于下蹲的0.5到站立的阈值1之间
        landingThreshold = Mathf.Clamp(verticalVelocity, -10, 0);
        landingThreshold /= 20;     //-0.5~0
        landingThreshold += 1f;     //0.5~1
        isLanding = true;   //上一帧处于跳跃姿态，则设为着陆状态，着陆状态下不能再跳跃
        playerPosture = PlayerPosture.Landing;
        yield return new WaitForSeconds(jumpCD);
        isLanding = false;
    }
    
    //设置动画参数
    void SetupAnimator()
    {
        if (playerPosture == PlayerPosture.Stand)
        {
            animator.SetFloat(postureHash, standThreshold, 0.1f, Time.deltaTime);
            switch (locomotionState)
            {
                case LocomotionState.Idle:
                    animator.SetFloat(moveSpeedHash, 0, 0.1f, Time.deltaTime);
                    break;
                case LocomotionState.Walk:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * walkSpeed, 0.1f, Time.deltaTime);
                    Debug.LogFormat("set walk speed is {0}",playerMovement.magnitude * walkSpeed);
                    break;
                case LocomotionState.Run:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * runSpeed, 0.1f, Time.deltaTime);
                    break;
            }
        }
        else if (playerPosture == PlayerPosture.Crouch)
        {
            animator.SetFloat(postureHash, crouchThreshold, 0.1f, Time.deltaTime);
            switch (locomotionState)
            {
                case LocomotionState.Idle:
                    animator.SetFloat(moveSpeedHash, 0, 0.1f, Time.deltaTime);
                    break;
                default:    //蹲着不区分行走或奔跑
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * crouchSpeed, 0.1f, Time.deltaTime);
                    break;
            }
        }
        else if (playerPosture == PlayerPosture.Jumping || playerPosture == PlayerPosture.Falling)
        {
            animator.SetFloat(postureHash, midairThreshold);
            animator.SetFloat(verticalVelHash, verticalVelocity);
            Debug.LogFormat("set Midair status");
        }
        else if (playerPosture == PlayerPosture.Landing)
        {
            //和PlayerPosture.Stand基本一致
            animator.SetFloat(postureHash, standThreshold, 0.03f, Time.deltaTime);    //这边下蹲动画和跳跃动画匹配不上，暂时不用
            switch (locomotionState)
            {
                case LocomotionState.Idle:
                    animator.SetFloat(moveSpeedHash, 0, 0.1f, Time.deltaTime);
                    break;
                case LocomotionState.Walk:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * walkSpeed, 0.1f, Time.deltaTime);
                    Debug.LogFormat("set walk speed is {0}",playerMovement.magnitude * walkSpeed);
                    break;
                case LocomotionState.Run:
                    animator.SetFloat(moveSpeedHash, playerMovement.magnitude * runSpeed, 0.1f, Time.deltaTime);
                    break;
            }
        }
        else if (playerPosture == PlayerPosture.Climbing)
        {
            animator.SetInteger("ClimbType",currentClimbParamter);
            playerTransform.rotation = Quaternion.Lerp(playerTransform.rotation,Quaternion.LookRotation(-playerSensor.climbHitNormal),0.5f );   //转向障碍物
            AnimatorController animatorController = animator.runtimeAnimatorController as AnimatorController;
            AnimatorStateMachine rootStateMachine = animatorController.layers[0].stateMachine; // 假设使用的是第一个Layer
            AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0); // 获取第一个Layer的当前状态
            if (currentState.IsName("ClimbLow"))
            {
                Debug.Log("========IsName clim low");
                currentClimbParamter = defaultClimbParameter;
                animator.MatchTarget(rightHandPosition,Quaternion.identity, AvatarTarget.RightHand,new MatchTargetWeightMask(Vector3.one,0f),0f,0.15f); //从动画0%开始对齐，15%地方完成对齐
                animator.MatchTarget(rightHandPosition+Vector3.up * 0.18f,Quaternion.identity, AvatarTarget.RightHand,new MatchTargetWeightMask(Vector3.up,0f),0.15f,0.58f);
            }
            else if(currentState.IsName("ClimbHigh"))
            {
                Debug.Log("========IsName clim HIGH");
                currentClimbParamter = defaultClimbParameter;
                animator.MatchTarget(rightHandPosition,Quaternion.identity, AvatarTarget.RightHand,new MatchTargetWeightMask(Vector3.one,0f),0.05f,0.53f);
            }
        }

        if (attackState == AttackState.Normal && playerPosture != PlayerPosture.Jumping)
        {
            //对玩家运动方向的x和z分量求arctan,得到玩家当前的运动方向和正前方向的夹角（弧度，和状态机的转向速度单位一致）
            float rad = Mathf.Atan2(playerMovement.x, playerMovement.z);
            animator.SetFloat(turnSpeedHash, rad, 0.1f, Time.deltaTime);
            playerTransform.Rotate(0, rad * 180 * Time.deltaTime, 0f);  //转向速度太慢，人为添加一个转向
        }
    }
    
    //计算离地前缓存帧的平均速度，游戏中平滑、去噪也是基于平均值
    Vector3 AverageVel(Vector3 newVel)
    {
        velCache[currentCacheIndex] = newVel;   //替换缓存帧最老的向量
        currentCacheIndex++;
        currentCacheIndex %= velCache.Length;
        Vector3 avarage = Vector3.zero;
        for (int i = 0; i < velCache.Length; i++)
        {
            avarage += velCache[i];
        }

        return avarage / velCache.Length;
    }

    //播放脚步声
    // void PlayFootStep()
    // {
    //     if (playerPosture != PlayerPosture.Jumping && playerPosture != PlayerPosture.Falling)
    //     {
    //         if (locomotionState == LocomotionState.Walk || locomotionState == LocomotionState.Run)
    //         {
    //             float currentFootCycle = Mathf.Repeat(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f); ;
    //             if ((lastFootCycle < 0.1 && currentFootCycle >= 0.1) || (currentFootCycle >= 0.6 && lastFootCycle < 0.6))
    //             {
    //                 playerSoundController.PlayFootStep();
    //             }
    //             lastFootCycle = currentFootCycle;
    //         }
    //     }
    // }
    
    //动画系统回调方法，接管rootmotion位移
    private void OnAnimatorMove()
    {
        if (playerPosture == PlayerPosture.Climbing)
        {
            //通过动画曲线取消IK，并且在这边手动禁用碰撞体
            capsuleCollider.enabled = false;
            animator.ApplyBuiltinRootMotion();  //将控制权还给root motion
            gravity = 0;
        } 
        else if (playerPosture != PlayerPosture.Jumping && playerPosture != PlayerPosture.Falling)
        {
            capsuleCollider.enabled = true;
            gravity = -15f;
            Vector3 playerDeltaMovement = animator.deltaPosition;
            playerDeltaMovement.y = verticalVelocity * Time.deltaTime;
            //Debug.LogFormat("playerDeltaMovement.y:{0}",playerDeltaMovement.y);
            playerTransform.position += playerDeltaMovement;
            averageVel = AverageVel(animator.velocity);
            //Debug.LogFormat("avgvel:{0},playerDeltaMovement：{1}",averageVel,playerDeltaMovement);
        }
        else
        {
            capsuleCollider.enabled = true;
            gravity = -15f;
            averageVel.y = verticalVelocity;
            //沿用地面速度，比如角色离地前几帧的平均速度，解决跳跃时水平方向移动异常问题
            Vector3 playerDeltaMovement = averageVel * Time.deltaTime;  //平均速度*时间，animator.deltaPosition会受帧率影响，跳跃前后帧率差异可能较大，平均值不具备参考性
            //Debug.LogFormat("playerDeltaMovement.y:{0}",playerDeltaMovement.y);
            playerTransform.position += playerDeltaMovement;
        }
    }

    private void Jump()
    {
        if (playerPosture == PlayerPosture.Stand && isJumpPress)
        {
            float velOffset;    //不同状态下翻越障碍物距离应该有差异
            switch (locomotionState)
            {
                case LocomotionState.Run:
                    velOffset = 1f;
                    break;
                case LocomotionState.Walk:
                    velOffset = 0.5f;
                    break;
                case LocomotionState.Idle:
                default:
                    velOffset = 0f;
                    break;
            }
            switch (playerSensor.ClimbDetect(playerTransform,PlayerMomentWorldSpace,velOffset))
            {
                case NextPlayerMovement.jump:
                    Debug.Log("jump here");
                    verticalVelocity = Mathf.Sqrt(-2 * gravity * maxHeight);        //自由落体公式v^2 = 2gh => v = sqrt(2gh)
                    break;
                case NextPlayerMovement.climbLow:
                    //动画攀爬时右手支撑，在进度15%时应该放在检测的障碍物边缘右边一点位置
                    rightHandPosition = playerSensor.ledge +
                                        Vector3.Cross(-playerSensor.climbHitNormal, Vector3.down) * 0.3f;   //叉乘得到右边位置
                    currentClimbParamter = lowClimbParamter;
                    isClimbReady = true;
                    break;
                case NextPlayerMovement.climbHigh:
                    currentClimbParamter = highClimbParamter;
                    isClimbReady = true;
                    break;
                case NextPlayerMovement.vault:
                    Debug.Log("========VAULT");
                    currentClimbParamter = vaultParamter;
                    isClimbReady = true;
                    break;
            }
            
        }
    }
}
