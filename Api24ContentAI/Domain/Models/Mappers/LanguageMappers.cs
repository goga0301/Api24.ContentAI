using Api24ContentAI.Domain.Entities;

namespace Api24ContentAI.Domain.Models.Mappers
{
    public static class LanguageMappers
    {
        public static Language ToEntity(this CreateLanguageModel model)
        {
            return new Language { Name = model.Name };
        }

        public static LanguageModel ToModel(this Language entity)
        {
            return new LanguageModel { Id = entity.Id, Name = entity.Name };
        }
    }
}
