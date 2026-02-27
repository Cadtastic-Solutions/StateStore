// Example usage for SQLite
var store = new StateStoreBuilder()
    .UseSqlite("Data Source=state.db")
    .UseJsonSerializer()
    .Build();

// Example usage for MongoDB
var mongoStore = new StateStoreBuilder()
    .UseMongo("mongodb://localhost:27017", "StateStoreDb", "StateCollection")
    .UseJsonSerializer()
    .Build();
