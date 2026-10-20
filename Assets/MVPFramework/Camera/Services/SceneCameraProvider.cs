namespace UnityMvp.Infrastructure.Camera
{
    public sealed class SceneCameraProvider : ICameraProvider
    {
        public SceneCameraProvider(UnityEngine.Camera camera)
        {
            if (!camera)
                throw new System.ArgumentNullException(nameof(camera));

            Current = camera;
        }

        public UnityEngine.Camera Current { get; }
    }
}
