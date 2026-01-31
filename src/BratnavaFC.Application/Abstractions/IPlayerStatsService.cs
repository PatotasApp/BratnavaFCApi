using BratnavaFC.Domain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BratnavaFC.Application.Abstractions
{
    public interface IPlayerStatsService
    {
        Task<List<PlayerStats>> EnrichPlayersAsync(List<Player> players);
    }
}
