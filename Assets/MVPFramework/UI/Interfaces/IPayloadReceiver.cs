namespace UnityMvp.Interfaces
{
    public interface IPayloadReceiver<in TData>
    {
        public void ReceivePayload(TData payload);
    }
}
