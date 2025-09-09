using MongoDB.Driver;

namespace spyglass_backend.Features.Links
{
	public class MongoLinkService(IMongoDatabase database)
	{
		private readonly IMongoCollection<Link> _linksCollection = database.GetCollection<Link>("links");

		public async Task<List<Link>> GetAsync() =>
			await _linksCollection.Find(_ => true).ToListAsync();

		public async Task<Link?> GetAsync(string id) =>
			await _linksCollection.Find(x => x.Id == id).FirstOrDefaultAsync();

		public async Task<List<Link>> GetAsync(string sortByField, SortDirection sortDirection = SortDirection.Ascending)
		{
			// If no sort field is provided, fall back to the unsorted method to avoid errors.
			if (string.IsNullOrWhiteSpace(sortByField))
			{
				return await GetAsync();
			}

			// 1. Create a SortDefinitionBuilder for our Link type.
			var builder = Builders<Link>.Sort;

			// 2. Use the builder to create the specific sort definition based on the parameters.
			var sortDefinition = sortDirection == SortDirection.Ascending
				? builder.Ascending(sortByField)
				: builder.Descending(sortByField);

			// 3. Apply the sort to the Find query and execute it.
			return await _linksCollection.Find(_ => true)
										 .Sort(sortDefinition)
										 .ToListAsync();
		}

		public async Task CreateAsync(Link newLink) =>
			await _linksCollection.InsertOneAsync(newLink);

		public async Task CreateManyAsync(IEnumerable<Link> newLinks) =>
			await _linksCollection.InsertManyAsync(newLinks);

		public async Task UpdateAsync(string id, Link updatedLink) =>
			await _linksCollection.ReplaceOneAsync(x => x.Id == id, updatedLink);

		public async Task RemoveAsync(string id) =>
			await _linksCollection.DeleteOneAsync(x => x.Id == id);

		public async Task RemoveAllAsync() =>
			await _linksCollection.DeleteManyAsync(_ => true);
	}
}
