using UnityEngine;
using Unity.Mathematics;
using System.Runtime.InteropServices;

public class SimpleSimulation : MonoBehaviour
{
    [Header("Time")]
    public float timeScale = 1;
    public int iterationsPerFrame;

    [Header("Fluid")]
    public float gravity;
    [Range(0, 1)] 
    public float smoothingRadius;
    public float targetDensity;
    public float pressureMultiplier;
    public float nearPressureMultiplier;
    public float viscosityMultiplier;
    [Header("Bounds")]
    public Vector2 boundsSize;
    public float collisionDamping = 0.95f;

    [Header("References")]
    public ComputeShader compute;

    [Header("Spawn")]
    public int particleCount;

    public Vector2 initialVelocity;
    public Vector2 spawnCentre;
    public Vector2 spawnSize;

    public ComputeBuffer positionBuffer { get; private set; }
    public ComputeBuffer velocityBuffer { get; private set; }
    public ComputeBuffer densityBuffer { get; private set; }
    ComputeBuffer predictedPositionBuffer;

    const int externalForcesKernel = 0;
    const int predictPositionsKernel = 1;
    const int densityKernel = 2;
    const int pressureKernel = 3;
    const int viscosityKernel = 4;
    const int updatePositionKernel = 5;

    private bool isPaused;
    private float2[] particleSpawnPositions;
    private float2[] particleSpawnVelocities;

    private float2[] particleCurrentPositions;

    void Start()
    {
        Debug.Log("Pause - Space, Reset - R");

        SetSpawnParameters();

        positionBuffer = new ComputeBuffer(particleCount, Marshal.SizeOf(typeof(float2)));
        predictedPositionBuffer = new ComputeBuffer(particleCount, Marshal.SizeOf(typeof(float2)));
        velocityBuffer = new ComputeBuffer(particleCount, Marshal.SizeOf(typeof(float2)));
        densityBuffer = new ComputeBuffer(particleCount, Marshal.SizeOf(typeof(float2)));

        SetInitialBufferData();

        SetBuffersToComputeShader();

        compute.SetInt("numParticles", particleCount);
    }

    private void SetBuffersToComputeShader()
    {
        compute.SetBuffer(externalForcesKernel, "Positions", positionBuffer);
        compute.SetBuffer(externalForcesKernel, "PredictedPositions", predictedPositionBuffer);
        compute.SetBuffer(externalForcesKernel, "Velocities", velocityBuffer);

        compute.SetBuffer(predictPositionsKernel, "Positions", positionBuffer);
        compute.SetBuffer(predictPositionsKernel, "PredictedPositions", predictedPositionBuffer);
        compute.SetBuffer(predictPositionsKernel, "Velocities", velocityBuffer);
        
        compute.SetBuffer(densityKernel, "Densities", densityBuffer);
        compute.SetBuffer(densityKernel, "PredictedPositions", predictedPositionBuffer);
        
        compute.SetBuffer(pressureKernel, "Densities", densityBuffer);
        compute.SetBuffer(pressureKernel, "PredictedPositions", predictedPositionBuffer);
        compute.SetBuffer(pressureKernel, "Velocities", velocityBuffer);
        
        compute.SetBuffer(viscosityKernel, "Densities", densityBuffer);
        compute.SetBuffer(viscosityKernel, "PredictedPositions", predictedPositionBuffer);
        compute.SetBuffer(viscosityKernel, "Velocities", velocityBuffer);

        compute.SetBuffer(updatePositionKernel, "Positions", positionBuffer);
        compute.SetBuffer(updatePositionKernel, "Velocities", velocityBuffer);
    }

    public void SetSpawnParameters()
    {
        particleSpawnPositions = new float2[particleCount];
        particleSpawnVelocities = new float2[particleCount];
        particleCurrentPositions = new float2[particleCount];
        for (int i = 0; i < particleCount; i++)
        {
            float2 pos = new float2(
                UnityEngine.Random.Range(spawnCentre.x - spawnSize.x * 0.5f, spawnCentre.x + spawnSize.x * 0.5f),
                UnityEngine.Random.Range(spawnCentre.y - spawnSize.y * 0.5f, spawnCentre.y + spawnSize.y * 0.5f)
            );
            particleSpawnPositions[i] = pos;

            particleSpawnVelocities[i] = initialVelocity;
        }
    }

    void Update()
    {
        RunSimulationFrame(Time.deltaTime);
        HandleInput();
    }

    void RunSimulationFrame(float frameTime)
    {
        if (!isPaused)
        {
            float timeStep = frameTime / iterationsPerFrame * timeScale;

            UpdateSettings(timeStep);

            for (int i = 0; i < iterationsPerFrame; i++)
            {
                RunSimulationStep();
            }
        }
    }

    void RunSimulationStep()
    {
        Dispatch(compute, particleCount, kernelIndex: externalForcesKernel);
        Dispatch(compute, particleCount, kernelIndex: predictPositionsKernel);
        Dispatch(compute, particleCount, kernelIndex: densityKernel);
        Dispatch(compute, particleCount, kernelIndex: pressureKernel);
        Dispatch(compute, particleCount, kernelIndex: viscosityKernel);
        Dispatch(compute, particleCount, kernelIndex: updatePositionKernel);
    }

    void UpdateTimeStep(float timeStep)
    {
        compute.SetFloat("timeStep", timeStep);
    }

    void UpdateSettings(float timeStep)
    {
        compute.SetFloat("timeStep", timeStep);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("targetDensity", targetDensity);
        compute.SetFloat("pressureMultiplier", pressureMultiplier);
        compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
        compute.SetFloat("viscosityStrength", viscosityMultiplier);
        compute.SetVector("boundsSize", boundsSize);

        compute.SetFloat("Poly6ScalingFactor", 35 / (16 * Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
        compute.SetFloat("SpikyPow3ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
        compute.SetFloat("SpikyPow2ScalingFactor", 3 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
        compute.SetFloat("SpikyPow3DerivativeScalingFactor", 3 / (Mathf.PI * Mathf.Pow(smoothingRadius, 3)));
        compute.SetFloat("SpikyPow2DerivativeScalingFactor", 2 / (Mathf.PI * Mathf.Pow(smoothingRadius, 3)));
    }

    void SetInitialBufferData()
    {
        positionBuffer.SetData(particleSpawnPositions);
        predictedPositionBuffer.SetData(particleSpawnPositions);
        velocityBuffer.SetData(particleSpawnVelocities);
    }

    void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            isPaused = !isPaused;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            isPaused = true;
            SetInitialBufferData();
            RunSimulationStep();
            SetInitialBufferData();
        }
    }


    void OnDestroy()
    {
        positionBuffer.Release();
        predictedPositionBuffer.Release();
        velocityBuffer.Release();
        densityBuffer.Release();
    }


    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0, 0, 0, 1.0f);
        Gizmos.DrawWireCube(Vector2.zero, boundsSize);
        if(positionBuffer == null)
            return;
        positionBuffer.GetData(particleCurrentPositions);
        for (int i = 0; i < particleCount; i++)
        {
            Gizmos.DrawSphere(new Vector3(particleCurrentPositions[i].x, particleCurrentPositions[i].y, 0), 0.05f);
        }
    }

    public static void CreateStructuredBuffer<T>(ref ComputeBuffer buffer, int count)
    {
        int stride = Marshal.SizeOf(typeof(T));
        bool createNewBuffer = buffer == null || !buffer.IsValid() || buffer.count != count || buffer.stride != stride;
        if (createNewBuffer)
        {
            buffer.Release();
            buffer = new ComputeBuffer(count, stride);
        }
    }

    public static void Dispatch(ComputeShader cs, int numIterationsX, int numIterationsY = 1, int numIterationsZ = 1, int kernelIndex = 0)
    {
        uint x, y, z;
        cs.GetKernelThreadGroupSizes(kernelIndex, out x, out y, out z);
        int numGroupsX = Mathf.CeilToInt(numIterationsX / (float)x);
        int numGroupsY = Mathf.CeilToInt(numIterationsY / (float)y);
        int numGroupsZ = Mathf.CeilToInt(numIterationsZ / (float)z);
        cs.Dispatch(kernelIndex, numGroupsX, numGroupsY, numGroupsZ);
    }
}