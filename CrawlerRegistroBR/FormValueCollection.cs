using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CrawlerRegistroBR
{
    public class FormUrlValueCollection : List<KeyValuePair<string, string>>
    {
        public void Add(string name, string value)
        {
            base.Add(new KeyValuePair<string, string>(name, value));
        }

        public void AddRange(List<KeyValuePair<string, string>> list)
        {
            base.AddRange(list);
        }

        public void Remove(string name)
        {
            base.Remove(this.First(item => item.Key.Equals(name)));
        }

        public void RemoveAll(string name)
        {
            base.RemoveAll(item => item.Key.Equals(name));
        }
    }
}
