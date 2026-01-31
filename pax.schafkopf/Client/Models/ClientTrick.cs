using pax.schafkopf.Shared;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace pax.schafkopf.Client.Models
{
    public class ClientTrick : INotifyPropertyChanged
    {
        private CardInfo _TrickCard;

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] String propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public CardInfo TrickCard
        {
            get { return _TrickCard; }
            set
            {
                if (value != _TrickCard)
                {
                    this._TrickCard = value;
                    NotifyPropertyChanged();
                }
            }
        }
    }
}
