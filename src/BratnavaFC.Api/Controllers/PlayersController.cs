// using BratnavaFC.Application.Abstractions;
// using BratnavaFC.Domain.Dtos.Players;
// using Microsoft.AspNetCore.Http;
// using Microsoft.AspNetCore.Mvc;

// namespace BratnavaFC.Api.Controllers
// {
//     [Route("api/[controller]")]
//     [ApiController]
//     public class PlayersController : ControllerBase
//     {
//         private readonly IPlayerService _playerService;

//         public PlayersController(IPlayerService playerService)
//         {
//             _playerService = playerService;
//         }

//         [HttpPost]
//         public async Task<IActionResult> CreatePlayer([FromBody] PlayerContracts.CreatePlayerRequest request, CancellationToken cancellationToken)
//         {
//             await _playerService.CreateAsync(request, cancellationToken);
//             return Ok();
//         }

//         [HttpDelete("{playerId:guid}")]
//         public async Task<IActionResult> DeletePlayer(Guid playerId, CancellationToken cancellationToken)
//         {
//             var request = new PlayerContracts.DeletePlayerRequest(playerId);
//             await _playerService.DeleteAsync(request, cancellationToken);
//             return Ok();
//         }

//         [HttpGet("{playerId:guid}")]
//         public async Task<IActionResult> GetPlayer(Guid playerId, CancellationToken cancellationToken)
//         {
//             var player = await _playerService.GetByIdAsync(playerId, cancellationToken);
//             return Ok(player);
//         }

//         [HttpPut("{playerId:guid}")]
//         public async Task<IActionResult> UpdatePlayer(Guid playerId, [FromBody] PlayerContracts.UpdatePlayerRequest request, CancellationToken cancellationToken)
//         {
//             await _playerService.UpdateAsync(playerId, request, cancellationToken);
//             return Ok();
//         }
//     }
// }
