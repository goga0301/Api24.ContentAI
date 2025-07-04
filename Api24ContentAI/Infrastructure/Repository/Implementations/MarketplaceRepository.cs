﻿using Api24ContentAI.Domain.Entities;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Infrastructure.Repository.DbContexts;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Threading.Tasks;

namespace Api24ContentAI.Infrastructure.Repository.Implementations
{
    public class MarketplaceRepository(ContentDbContext dbContext, IConfiguration configuration) : GenericRepository<Marketplace>(dbContext), IMarketplaceRepository
    {
        private readonly string connectionString = configuration.GetSection("DatabaseOptions:ConnectionString").Value ?? "";

        public async Task UpdateBalance(Guid uniqueKey, RequestType requestType)
        {
            using NpgsqlConnection connection = new(connectionString);
            await connection.OpenAsync();

            try
            {
                string updateQuery = requestType switch
                {
                    RequestType.Content => @"UPDATE ""ContentDb"".""Marketplaces""
                                             SET ""ContentLimit"" = ""ContentLimit"" - 1
                                             WHERE ""Id"" = @Id;",

                    RequestType.Translate => @"UPDATE ""ContentDb"".""Marketplaces""
                                               SET ""TranslateLimit"" = ""TranslateLimit"" - 1
                                               WHERE ""Id"" = @Id;",

                    RequestType.Copyright => @"UPDATE ""ContentDb"".""Marketplaces""
                                               SET ""CopyrightLimit"" = ""CopyrightLimit"" - 1
                                               WHERE ""Id"" = @Id;",

                    RequestType.VideoScript => @"UPDATE ""ContentDb"".""Marketplaces""
                                                 SET ""VideoScriptLimit"" = ""VideoScriptLimit"" - 1
                                                 WHERE ""Id"" = @Id;",

                    RequestType.Lawyer => @"UPDATE ""ContentDb"".""Marketplaces""
                                            SET ""LawyerLimit"" = ""LawyerLimit"" - 1
                                            WHERE ""Id"" = @Id;",

                    RequestType.EnhanceTranslate => @"UPDATE ""ContentDb"".""Marketplaces""
                                            SET ""EnhanceTranslateLimit"" = ""EnhanceTranslateLimit"" - 1
                                            WHERE ""Id"" = @Id;",
                    RequestType.Email => throw new NotImplementedException(),
                    _ => throw new Exception("Incorect request type")
                };

                _ = await connection.ExecuteAsync(updateQuery, new { Id = uniqueKey });
            }
            finally
            {
                await connection.CloseAsync();
            }
        }
    }
}

