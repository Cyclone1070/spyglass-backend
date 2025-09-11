using MongoDB.Driver;

namespace spyglass_backend.Features.Search
{
	public class MongoResultService(IMongoDatabase database)
	{
		private readonly IMongoCollection<StoredResult> _resultsCollection = database.GetCollection<StoredResult>("results");

		public async Task<StoredResult?> GetAsync(string query) =>
			await _resultsCollection.Find(x => x.Query == query).FirstOrDefaultAsync();

		public async Task CreateAsync(StoredResult newStoredResult) =>
			await _resultsCollection.InsertOneAsync(newStoredResult);

		public async Task RemoveAsync(string query) =>
			await _resultsCollection.DeleteOneAsync(x => x.Query == query);

		public async Task RemoveAllAsync() =>
			await _resultsCollection.DeleteManyAsync(_ => true);
	}
}
