using System.Collections.Generic;
using System.Threading.Tasks;
using BratnavaFC.Domain.Models;

namespace BratnavaFC.Application.Abstractions;

public interface IPlayerStatsService
{
    Task<List<PlayerStats>> EnrichPlayersAsync(List<Player> players);
}   
