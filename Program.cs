using System;
using System.Collections.Generic;
using System.Linq;
using Avalara.AvaTax.RestClient;

namespace ItemMasterLoad
{
    class Program
    {
        //These constants need to be changed to your values.
        //private const string SBX_USERNAME = "Your Username Here";
        private const string SBX_USERNAME = "Sandbox Username";
        private const string SBX_PASSWORD = "Sandbox Password";
        private const string PRD_USERNAME = "Production Username";
        private const string PRD_PASSWORD = "Production Password";

        //Change this value to your company ID.
        private const int SBX_COMPANY_ID = 12345;
        private const int PRD_COMPANY_ID = 12345;

        //Which environment do you wish to use? Change this value for Sandbox or Production.
        private const AvaTaxEnvironment prdEnvironment = AvaTaxEnvironment.Production;
        private const AvaTaxEnvironment sbxEnvironment = AvaTaxEnvironment.Sandbox;

        static void Main(string[] args)
        {
            //Pull SBX Items, Classifications, and Parameters.

            //Instantiate REST client
            var sbxClient =
                new AvaTaxClient("ItemMasterExtract", "v0.1",
                    ".NET Machine", sbxEnvironment)
                .WithSecurity(SBX_USERNAME, SBX_PASSWORD);
            var prdClient =
                new AvaTaxClient("ItemMasterLoad", "v0.1", ".NET Machine", prdEnvironment)
                .WithSecurity(PRD_USERNAME, PRD_PASSWORD);

            var itemModels = new List<ItemModel>();
            var nexus = new List<NexusModel>();

            //Load a list of Classification Systems into memory for easy lookup.
            var classificationSystems = sbxClient.ListProductClassificationSystems(string.Empty, null, null, string.Empty).value;

            ExtractSbxNexus(sbxClient, nexus);
            UpsertPrdNexus(prdClient, nexus);
            ExtractSbxItems(sbxClient, itemModels);            
            UpsertItems(prdClient, itemModels);
        }

        private static void ExtractSbxNexus(AvaTaxClient sbxClient, List<NexusModel> nexus)
        {
            nexus.AddRange(sbxClient.ListNexusByCompany(SBX_COMPANY_ID, "nexusTaxTypeGroup EQ LandedCost", string.Empty, null, null, string.Empty).value);
        }

        private static void UpsertPrdNexus(AvaTaxClient prdClient, List<NexusModel> nexus)
        {
            //Get existing PRD nexus.
            var prdNexus = prdClient.ListNexusByCompany(PRD_COMPANY_ID, "nexusTaxTypeGroup EQ LandedCost", string.Empty, null, null, string.Empty).value;

            //Change the company ID to PRD.
            foreach (NexusModel indNexus in nexus)
            {
                //Check if the nexus already exists in the company. 
                if(prdNexus.Select(pn=>pn.country == indNexus.country).Any())
                {
                    continue;
                }

                try
                {
                    indNexus.companyId = PRD_COMPANY_ID;
                    var nexusToAdd = new List<NexusModel>{ indNexus };
                    prdClient.CreateNexus(PRD_COMPANY_ID, nexusToAdd);
                }
                catch (AvaTaxError exc)
                {
                    Console.WriteLine(string.Format("Error in adding nexus", exc.Message, exc.InnerException));
                    Console.WriteLine(string.Format("More information: {0}", exc.error));
                    continue;
                }
                catch (Exception exc)
                {
                    Console.WriteLine(string.Format("Other error occurred in loading nexus: {0}", exc.Message));
                }
            }
        }

        private static void ExtractSbxItems(AvaTaxClient sbxClient, List<ItemModel> itemModels)
        {
            //Get the fetch count of objects for the Sandbox company.
            int fetchCount = sbxClient.QueryItems(string.Format("companyId EQ {0}", SBX_COMPANY_ID), 
                "classifications, parameters", 
                null, 
                null, 
                string.Empty)
                .count;

            //Pull 1000 objects at a time.
            for (int i = 0; i < fetchCount; i += 1000)
            {
                itemModels.AddRange(sbxClient.QueryItems(string.Format("companyId EQ {0}", SBX_COMPANY_ID), "classifications, parameters", null, i, string.Empty).value);
            }
        }

        private static void UpsertItems(AvaTaxClient client, List<ItemModel> itemModels)
        {
            foreach (ItemModel newItemModel in itemModels)
            {
                try
                {
                    //Does the item already exist in this company?
                    var existingItem = client.QueryItems(string.Format("itemCode EQ \"{0}\" AND companyId EQ {1}", newItemModel.itemCode, PRD_COMPANY_ID), string.Empty, null, null, string.Empty).value.FirstOrDefault();

                    //Yes, the item exists. Load the HS Codes to this item.
                    if (existingItem != null)
                    {
                        var existingClassifications = client.ListItemClassifications(PRD_COMPANY_ID, existingItem.id, string.Empty, null, null, string.Empty).value;

                        //Check if there are Classifications.
                        if (newItemModel.classifications != null)
                        {
                            //Yes, load the Classifications. 
                            foreach (var cm in newItemModel.classifications)
                            {
                                ItemClassificationInputModel clsInputModel = new ItemClassificationInputModel { productCode = cm.productCode, systemCode = cm.systemCode };

                                //Does the classification already exist? And is it the same?
                                var sameClassificationSystem = existingClassifications.Where(ec => ec.systemCode == cm.systemCode).FirstOrDefault();
                                var exactSameClassification = existingClassifications.Where(ec => ec.systemCode == cm.systemCode && ec.productCode == cm.productCode).FirstOrDefault();

                                //Classification does not exist. Add it.
                                if (sameClassificationSystem == null)
                                {
                                    client.CreateItemClassifications(PRD_COMPANY_ID, existingItem.id, new List<ItemClassificationInputModel> { clsInputModel });

                                    continue;
                                }

                                //Classification exists, but is different. Update it.
                                if (sameClassificationSystem != null &&
                                    exactSameClassification == null)
                                {
                                    client.UpdateItemClassification(PRD_COMPANY_ID, existingItem.id, sameClassificationSystem.id.Value, clsInputModel);
                                }
                            }
                        }
                    }
                    else
                    {
                        //Item does not yet exist. Create a new Item.
                        newItemModel.companyId = PRD_COMPANY_ID;
                        client.CreateItems(PRD_COMPANY_ID, new List<ItemModel>() { newItemModel });
                    }
                }
                catch (AvaTaxError exc)
                {
                    Console.WriteLine(string.Format("Error loading/updating item {0}", newItemModel.id));
                    Console.WriteLine(string.Format("More information: {0}", exc.error));
                }
                catch (Exception exc)
                {
                    Console.WriteLine(exc.Message);
                }
            }
        }       
    }
}
