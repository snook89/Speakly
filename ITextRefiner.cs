using System.Threading.Tasks;

namespace Speakly.Services
{
    public interface ITextRefiner
    {
        Task<string> RefineTextAsync(RefinementRequest request);
    }
}
