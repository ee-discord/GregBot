namespace ForumCrawler.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class PermanentScore : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.ScoreUsers", "PermanentScore", c => c.Double(nullable: false));
        }
        
        public override void Down()
        {
            DropColumn("dbo.ScoreUsers", "PermanentScore");
        }
    }
}
