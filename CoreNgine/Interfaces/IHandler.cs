using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreNgine.Interfaces
{
    public interface IHandler<in T>
    {
        void OnHandleData(T data)
        {

        }
    }
}
