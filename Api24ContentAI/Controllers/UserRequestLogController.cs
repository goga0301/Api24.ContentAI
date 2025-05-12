using Api24ContentAI.Domain.Models;
using Api24ContentAI.Domain.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Api24ContentAI.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class UserRequestLogController(IUserRequestLogService requestLogService) : ControllerBase
    {
        private readonly IUserRequestLogService _userRequestLogService = requestLogService;

        [HttpGet]
        public async Task<List<UserRequestLogModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _userRequestLogService.GetAll(cancellationToken);
        }

        [HttpGet("{id}")]
        public async Task<UserRequestLogModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            return await _userRequestLogService.GetById(id, cancellationToken);
        }

        [HttpGet("by-user/{userId}")]
        public async Task<List<UserRequestLogModel>> GetByUserId(string userId, CancellationToken cancellationToken)
        {
            return await _userRequestLogService.GetByUserId(userId, cancellationToken);
        }

        [HttpGet("count/{userId}")]
        public async Task<LogCountModel> CountByUserId(string userId, CancellationToken cancellationToken)
        {
            return await _userRequestLogService.CountByUserId(userId, cancellationToken);
        }
    }
}
