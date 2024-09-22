using Api24ContentAI.Domain.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using System;
using Api24ContentAI.Domain.Service;
using Api24ContentAI.Domain.Repository;
using Api24ContentAI.Domain.Models.Mappers;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Api24ContentAI.Infrastructure.Service.Implementations
{
    public class ProductCategoryService : IProductCategoryService
    {
        private readonly IProductCategoryRepository _productCategoryRepository;
        private readonly IApi24Service _api24Service;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public ProductCategoryService(IProductCategoryRepository productCategoryRepository, IApi24Service api24Service, IServiceScopeFactory serviceScopeFactory )
        {
            _productCategoryRepository = productCategoryRepository;
            _api24Service = api24Service;
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task<Guid> Create(CreateProductCategoryModel productCategory, CancellationToken cancellationToken)
        {
            return await _productCategoryRepository.Create(productCategory.ToEntity(), cancellationToken);
        }

        public async Task Delete(Guid id, CancellationToken cancellationToken)
        {
            await _productCategoryRepository.Delete(id, cancellationToken);
        }

        public async Task DeleteByApi24Id(string api24Id, CancellationToken cancellationToken)
        {
            var entity = await _productCategoryRepository.GetAll().FirstOrDefaultAsync(x => x.Api24Id == api24Id);
            await _productCategoryRepository.Delete(entity.Id, cancellationToken);
        }

        public async Task<List<ProductCategoryModel>> GetAll(CancellationToken cancellationToken)
        {
            return await _productCategoryRepository.GetAll()
                            .Select(x => x.ToModel()).ToListAsync(cancellationToken);
        }

        public async Task<ProductCategoryModel> GetById(Guid id, CancellationToken cancellationToken)
        {
            var productCategory = await _productCategoryRepository.GetById(id, cancellationToken);
            if (productCategory == null)
            {
                throw new Exception("პროდუქტის კატეგორია არ მოიძებნა");
            }
            return productCategory.ToModel();
        }

        public async Task SyncCategories(CancellationToken cancellationToken)
        {
            var categoriesFromApi24 = await _api24Service.GetCategories(cancellationToken);
            categoriesFromApi24 = await TranslateCategoryName(categoriesFromApi24, cancellationToken);
            var categories = await _productCategoryRepository.GetAll().ToListAsync(cancellationToken);
            //var toDelete = categories.Where(x => !categoriesFromApi24.Exists(c => c.Id == x.Api24Id));
            //var toAdd = categoriesFromApi24.Where(x => !categories.Exists(c => x.Id == c.Api24Id));
            //var ToUpdate = categories.Where(x => categoriesFromApi24.Exists(c => c.Id == x.Api24Id && (x.Name != c.Name || x.NameEng != c.NameEng)));

            var categoriesFromApi24Ids = new HashSet<string>(categoriesFromApi24.Select(c => c.Id));
            var categoriesIds = new HashSet<string>(categories.Select(c => c.Api24Id));

            var toDeleteIds = categoriesIds.Except(categoriesFromApi24Ids);
            var toAdd = categoriesFromApi24.Where(c => !categoriesIds.Contains(c.Id));
            //var toUpdate = categories.Where(c => categoriesFromApi24Ids.Contains(c.Api24Id) &&
            //                                     (c.Name != categoriesFromApi24.First(x => x.Id == c.Api24Id).Name ||
            //                                      c.NameEng != categoriesFromApi24.First(x => x.Id == c.Api24Id).NameEng)
            var toUpdate = categories.Where(x => categoriesFromApi24.Exists
                                                (c => c.Id == x.Api24Id &&
                                                (x.Name != c.Name || x.NameEng != c.NameEng)));
            var toDelete = categories.Where(c => toDeleteIds.Contains(c.Api24Id));

            foreach (var id in toDeleteIds)
            {
                await DeleteByApi24Id(id, cancellationToken);
            }

            foreach (var category in toAdd)
            {
                var cat = new CreateProductCategoryModel()
                {
                    Name = category.Name,
                    NameEng = category.NameEng,
                    Api24Id = category.Id
                };
                await Create(cat, cancellationToken);
            }

            foreach(var category in toUpdate)
            {
                var catFromApi24 = categoriesFromApi24.Find(x => x.Id == category.Api24Id);
                var catUpdate = new UpdateProductCategoryModel()
                {
                    Id = category.Id,
                    Name = catFromApi24.Name,
                    NameEng = catFromApi24.NameEng,
                    Api24Id = catFromApi24.Id
                };
                await Update(catUpdate, cancellationToken);
            }
        }

        private async Task<List<CategoryResponse>> TranslateCategoryName(List<CategoryResponse> categories, CancellationToken cancellationToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var _userContentService = scope.ServiceProvider.GetRequiredService<IUserContentService>();
            foreach (var category in categories)
            {
                var req = new UserTranslateRequest
                {
                    Description = category.Name,
                    SourceLanguageId = 1,
                    LanguageId = 2,
                    IsPdf = false
                };
                var translated = await _userContentService.ChunkedTranslate(req, "ae77823a-e212-4b9f-ab1a-a5c9b727a581", cancellationToken);
                category.NameEng = translated.Text.Replace("<br>", "");
            }
            return categories;
        }

        public async Task Update(UpdateProductCategoryModel productCategory, CancellationToken cancellationToken)
        {
            var entity = await _productCategoryRepository.GetById(productCategory.Id, cancellationToken);
            entity.Name = productCategory.Name;

            await _productCategoryRepository.Update(entity, cancellationToken);
        }
    }
}
