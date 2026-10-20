namespace UnityMvp.Interfaces
{
    public interface IModelSetter<in TModel>
    {
        public void SetModel(TModel model);
    }
}
