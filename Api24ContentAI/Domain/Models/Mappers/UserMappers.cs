using Api24ContentAI.Domain.Entities;

namespace Api24ContentAI.Domain.Models.Mappers
{
    public static class UserMappers
    {
        public static UserModel ToModel(this User entity)
        {
            return new UserModel
            {
                Id = entity.Id,
                UserName = entity.UserName,
                Email = entity.Email,
                PhoneNUmber = entity.PhoneNumber,
                FirstName = entity.FirstName,
                LastName = entity.LastName,
                RoleId = entity.RoleId,
                RoleName = entity.Role.Name,
                Balance = entity.UserBalance.Balance
            };
        }
    }
}
