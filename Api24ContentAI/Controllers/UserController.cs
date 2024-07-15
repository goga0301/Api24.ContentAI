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
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpGet]
        public async Task<List<UserModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _userService.GetAll(cancellationToken);
        }

        [HttpGet("{id}")]
        public async Task<UserModel> GetById(string id, CancellationToken cancellationToken)
        {
            return await _userService.GetById(id, cancellationToken);
        }

        [HttpPut]
        public async Task<IActionResult> Update([FromBody] UpdateUserModel model, CancellationToken cancellationToken)
        {
            try
            {
                await _userService.Update(model, cancellationToken);
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPatch]
        public async Task<IActionResult> ChangePassword([FromBody] ChangeUserPasswordModel model, CancellationToken cancellationToken)
        {
            try
            {
                var result = await _userService.ChangePassword(model, cancellationToken);
                if (result)
                {
                    return Ok(result);
                }
                return BadRequest("Changing password has failed. Current Password is not correct");
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
        {
            try
            {
                await _userService.Delete(id, cancellationToken);
                return Ok();
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}